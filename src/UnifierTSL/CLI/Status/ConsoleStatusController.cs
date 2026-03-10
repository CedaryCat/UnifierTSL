using UnifierTSL.Servers;

namespace UnifierTSL.CLI.Status
{
    internal sealed class ConsoleStatusController : IDisposable
    {
        private readonly ConsoleStatusService statusService;
        private readonly ConsoleStatusPublisher publisher;
        private readonly Lock scopeSync = new();
        private readonly HashSet<ConsoleActivityScope> activeScopes = [];
        private int disposed;

        public ConsoleStatusController(
            ServerContext? server,
            Action<ConsoleStatusPublication> sink,
            Func<bool>? shouldPublish = null) {
            ArgumentNullException.ThrowIfNull(sink);

            statusService = new ConsoleStatusService(server);
            statusService.RegisterBaseline(RuntimeConsoleStatusProvider.CreateBaseline(server));
            publisher = new ConsoleStatusPublisher(statusService, sink, shouldPublish);
        }

        public IDisposable BeginActivity(string category, string message) {
            if (Volatile.Read(ref disposed) != 0) {
                return NoopDisposable.Instance;
            }

            ConsoleActivityScope scope = new(statusService, category, message, OnScopeDisposed);
            if (!TryRegisterScope(scope)) {
                scope.Dispose();
                return NoopDisposable.Instance;
            }

            scope.Start();
            return scope;
        }

        public void ReplayCurrent() {
            publisher.ReplayCurrent();
        }

        public void ResetChangeTracking() {
            statusService.ResetChangeTracking();
        }

        public void Dispose() {
            if (Interlocked.Exchange(ref disposed, 1) != 0) {
                return;
            }

            try {
                publisher.Dispose();
            }
            catch {
            }

            ConsoleActivityScope[] scopes;
            lock (scopeSync) {
                scopes = [.. activeScopes];
                activeScopes.Clear();
            }

            foreach (ConsoleActivityScope scope in scopes) {
                scope.Dispose();
            }

            statusService.Dispose();
        }

        private bool TryRegisterScope(ConsoleActivityScope scope) {
            lock (scopeSync) {
                if (Volatile.Read(ref disposed) != 0) {
                    return false;
                }

                activeScopes.Add(scope);
                return true;
            }
        }

        private void OnScopeDisposed(ConsoleActivityScope scope) {
            lock (scopeSync) {
                activeScopes.Remove(scope);
            }
        }

        private sealed class NoopDisposable : IDisposable
        {
            public static readonly NoopDisposable Instance = new();

            public void Dispose() { }
        }
    }
}
