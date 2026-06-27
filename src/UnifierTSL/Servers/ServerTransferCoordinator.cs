namespace UnifierTSL.Servers
{
    internal static class ServerTransferCoordinator
    {
        private static readonly Lock gate = new();
        private static readonly Dictionary<int, SemaphoreSlim> playerGates = [];

        public static async Task<ServerTransferResult> TransferAsync(ServerTransferRequest request, CancellationToken cancellationToken) {
            ArgumentNullException.ThrowIfNull(request);
            if ((uint)request.PlayerId >= (uint)ServerRuntime.Clients.Length) {
                return ServerTransferResult.Failure(request.PlayerId, null, request.Target, "Player id is outside the client range.");
            }

            var playerGate = GetPlayerGate(request.PlayerId);
            await playerGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try {
                var source = ServerRuntime.GetCurrentServer(request.PlayerId);
                if (source is null) {
                    return ServerTransferResult.Failure(request.PlayerId, null, request.Target, "Player is not attached to a server.");
                }
                if (ReferenceEquals(source, request.Target)) {
                    return ServerTransferResult.Success(request.PlayerId, source, request.Target);
                }
                var options = request.Options ?? ServerTransferOptions.Default;
                if (options.RequireRunning && !request.Target.IsRunning && !options.AllowTransientTarget) {
                    return ServerTransferResult.Failure(request.PlayerId, source, request.Target, "Target server is not running.");
                }

                return await ServerDispatchRendezvous.InvokeAsync(
                    [source.Dispatcher, request.Target.Dispatcher],
                    () => UnifiedServerCoordinator.TransferPlayer(request, source),
                    cancellationToken).ConfigureAwait(false);
            }
            finally {
                playerGate.Release();
            }
        }

        private static SemaphoreSlim GetPlayerGate(int playerId) {
            lock (gate) {
                if (!playerGates.TryGetValue(playerId, out var playerGate)) {
                    playerGate = new(1, 1);
                    playerGates.Add(playerId, playerGate);
                }

                return playerGate;
            }
        }
    }

    internal static class ServerDispatchRendezvous
    {
        public static Task<T> InvokeAsync<T>(IReadOnlyList<ServerDispatcher> dispatchers, Func<T> action, CancellationToken cancellationToken) {
            ArgumentNullException.ThrowIfNull(dispatchers);
            ArgumentNullException.ThrowIfNull(action);
            cancellationToken.ThrowIfCancellationRequested();

            var representatives = dispatchers
                .GroupBy(static dispatcher => dispatcher.Domain)
                .Select(static group => group.First())
                .ToArray();
            if (representatives.Length == 1) {
                return representatives[0].InvokeAsync(action, cancellationToken);
            }

            var coordination = new Coordination<T>(representatives.Length, action, cancellationToken);
            // A dispatcher that is already executing must arrive last: invoking it inline before the other
            // domains are queued would block its own caller at the rendezvous and prevent registration.
            foreach (var dispatcher in representatives.OrderBy(static dispatcher => dispatcher.CheckAccess())) {
                try {
                    coordination.Observe(dispatcher.InvokeAsync(coordination.Arrive, cancellationToken));
                }
                catch (Exception ex) {
                    coordination.Abort(ex);
                    break;
                }
            }

            return coordination.Task;
        }

        private sealed class Coordination<T>
        {
            private readonly Lock gate = new();
            private readonly ManualResetEventSlim released = new();
            private readonly TaskCompletionSource<T> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly Func<T> action;
            private readonly int participantCount;
            private readonly CancellationToken cancellationToken;
            private readonly CancellationTokenRegistration cancellationRegistration;
            private int arrivals;
            private bool finished;

            public Coordination(int participantCount, Func<T> action, CancellationToken cancellationToken) {
                this.participantCount = participantCount;
                this.action = action;
                this.cancellationToken = cancellationToken;
                cancellationRegistration = cancellationToken.Register(
                    static state => ((Coordination<T>)state!).AbortCancellation(),
                    this);
            }

            public Task<T> Task => completion.Task;

            public void Arrive() {
                var commits = false;
                lock (gate) {
                    if (finished) {
                        return;
                    }

                    arrivals++;
                    if (arrivals == participantCount) {
                        finished = true;
                        commits = true;
                    }
                }

                if (!commits) {
                    // This is a deliberate update-thread safe point, not an asynchronous wait. Every arrived
                    // physical domain remains quiescent until the last domain commits or coordination aborts.
                    released.Wait();
                    return;
                }

                try {
                    completion.TrySetResult(action());
                }
                catch (Exception ex) {
                    completion.TrySetException(ex);
                }
                finally {
                    cancellationRegistration.Dispose();
                    released.Set();
                }
            }

            public void Observe(Task dispatch) => _ = ObserveAsync(dispatch);

            private async Task ObserveAsync(Task dispatch) {
                try {
                    await dispatch.ConfigureAwait(false);
                }
                catch (Exception ex) {
                    Abort(ex);
                }
            }

            public void Abort(Exception exception) {
                lock (gate) {
                    if (finished) {
                        return;
                    }

                    finished = true;
                }

                if (!cancellationToken.IsCancellationRequested) {
                    cancellationRegistration.Dispose();
                }
                if (exception is OperationCanceledException canceled) {
                    completion.TrySetCanceled(canceled.CancellationToken);
                }
                else {
                    completion.TrySetException(exception);
                }
                released.Set();
            }

            private void AbortCancellation() => Abort(new OperationCanceledException(cancellationToken));
        }
    }
}
