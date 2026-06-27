using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Kaleido.Context;
using Kaleido.Model.Ids;
using Kaleido.Model.Instances;
using Kaleido.Model.Planning;
using UnifierTSL.Servers;

namespace Kaleido.Runtime
{
    public sealed class RealmInstanceRegistry
    {
        private readonly Lock gate = new();
        private readonly Dictionary<RealmKey, PendingCreation> pending = [];
        private readonly CancellationToken lifetimeToken;
        private ImmutableDictionary<RealmKey, RealmRuntime> byKey = ImmutableDictionary<RealmKey, RealmRuntime>.Empty;
        private ImmutableDictionary<ServerContext, RealmRuntime> byServer = ImmutableDictionary<ServerContext, RealmRuntime>.Empty;
        private bool stopping;

        public RealmInstanceRegistry(CancellationToken lifetimeToken = default) {
            this.lifetimeToken = lifetimeToken;
        }

        public ImmutableArray<RealmInstance> Instances => [.. Volatile.Read(ref byKey).Values.Select(static runtime => runtime.Instance)];

        internal ImmutableArray<RealmRuntime> Runtimes => [.. Volatile.Read(ref byKey).Values];

        public bool TryGet(RealmKey key, [NotNullWhen(true)] out RealmInstance? instance) {
            if (TryGetRuntime(key, out var runtime)) {
                instance = runtime.Instance;
                return true;
            }

            instance = null;
            return false;
        }

        public bool TryGet(ServerContext server, [NotNullWhen(true)] out RealmInstance? instance) {
            if (TryGetRuntime(server, out var runtime)) {
                instance = runtime.Instance;
                return true;
            }

            instance = null;
            return false;
        }

        internal bool TryGetRuntime(RealmKey key, [NotNullWhen(true)] out RealmRuntime? runtime)
            => Volatile.Read(ref byKey).TryGetValue(key, out runtime);

        internal bool TryGetRuntime(ServerContext server, [NotNullWhen(true)] out RealmRuntime? runtime)
            => Volatile.Read(ref byServer).TryGetValue(server, out runtime);

        internal async Task<RealmRuntime> GetOrCreateAsync(
            RealmPlan plan,
            Func<RealmPlan, CancellationToken, Task<RealmRuntime>> factory,
            CancellationToken cancellationToken) {

            while (true) {
                PendingCreation? creation = null;
                Task? removal = null;
                var startsCreation = false;
                lock (gate) {
                    ThrowIfStopping();
                    if (byKey.TryGetValue(plan.Key, out var existing)) {
                        if (!existing.IsRetiring) {
                            return existing;
                        }

                        removal = existing.Removed;
                    }
                    else {
                        if (!pending.TryGetValue(plan.Key, out creation)) {
                            creation = new();
                            pending.Add(plan.Key, creation);
                            startsCreation = true;
                        }
                    }
                }

                if (removal is not null) {
                    await removal.WaitAsync(cancellationToken).ConfigureAwait(false);
                    continue;
                }
                if (startsCreation) {
                    _ = CreateAndPublishAsync(plan, factory, creation!);
                }

                return await creation!.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        internal async Task<RealmAdmission> AcquireAsync(
            RealmPlan plan,
            Func<RealmPlan, CancellationToken, Task<RealmRuntime>> factory,
            CancellationToken cancellationToken) {

            while (true) {
                PendingCreation? creation = null;
                Task? removal = null;
                var startsCreation = false;
                lock (gate) {
                    ThrowIfStopping();
                    if (byKey.TryGetValue(plan.Key, out var existing)) {
                        if (existing.TryEnterAdmission()) {
                            return new(existing);
                        }

                        removal = existing.Removed;
                    }
                    else {
                        if (!pending.TryGetValue(plan.Key, out creation)) {
                            creation = new();
                            pending.Add(plan.Key, creation);
                            startsCreation = true;
                        }

                        creation.Admissions++;
                    }
                }

                if (removal is not null) {
                    await removal.WaitAsync(cancellationToken).ConfigureAwait(false);
                    continue;
                }
                if (startsCreation) {
                    _ = CreateAndPublishAsync(plan, factory, creation!);
                }

                return await AwaitAdmissionAsync(creation!, cancellationToken).ConfigureAwait(false);
            }
        }

        internal bool Remove(RealmRuntime runtime) {
            lock (gate) {
                if (!byKey.TryGetValue(runtime.Plan.Key, out var current) || !ReferenceEquals(current, runtime)) {
                    return false;
                }

                Volatile.Write(ref byKey, byKey.Remove(runtime.Plan.Key));
                if (byServer.TryGetValue(runtime.Server, out current) && ReferenceEquals(current, runtime)) {
                    Volatile.Write(ref byServer, byServer.Remove(runtime.Server));
                }

                if (ReferenceEquals(runtime.Server.TryGetRealmInstance(), runtime.Instance)) {
                    runtime.Server.ClearRealmInstance();
                }

                pending.Remove(runtime.Plan.Key);
                runtime.MarkRemoved();
                return true;
            }
        }

        internal async Task StopAsync() {
            Task<RealmRuntime>[] creations;
            lock (gate) {
                stopping = true;
                creations = [.. pending.Values.Select(static creation => creation.Task)];
            }

            try {
                await Task.WhenAll(creations).ConfigureAwait(false);
            }
            catch {
            }
        }

        private async Task CreateAndPublishAsync(
            RealmPlan plan,
            Func<RealmPlan, CancellationToken, Task<RealmRuntime>> factory,
            PendingCreation creation) {

            try {
                var runtime = await factory(plan, lifetimeToken).ConfigureAwait(false);
                lock (gate) {
                    runtime.AddAdmissions(creation.Admissions);
                    creation.PublishedRuntime = runtime;
                    Volatile.Write(ref byKey, byKey.SetItem(plan.Key, runtime));
                    Volatile.Write(ref byServer, byServer.SetItem(runtime.Server, runtime));
                    runtime.Server.SetRealmInstance(runtime.Instance);
                    pending.Remove(plan.Key);
                }

                creation.Source.SetResult(runtime);
            }
            catch (OperationCanceledException) when (lifetimeToken.IsCancellationRequested) {
                lock (gate) {
                    pending.Remove(plan.Key);
                }

                creation.Source.SetCanceled(lifetimeToken);
            }
            catch (Exception ex) {
                lock (gate) {
                    pending.Remove(plan.Key);
                }

                creation.Source.SetException(ex);
            }
        }

        private async Task<RealmAdmission> AwaitAdmissionAsync(PendingCreation creation, CancellationToken cancellationToken) {
            try {
                return new(await creation.Task.WaitAsync(cancellationToken).ConfigureAwait(false));
            }
            catch {
                RealmRuntime? published;
                lock (gate) {
                    published = creation.PublishedRuntime;
                    if (published is null) {
                        creation.Admissions--;
                    }
                }

                published?.ExitAdmission();
                throw;
            }
        }

        private void ThrowIfStopping() {
            if (stopping) {
                throw new ObjectDisposedException(nameof(RealmInstanceRegistry));
            }
        }

        private sealed class PendingCreation
        {
            public TaskCompletionSource<RealmRuntime> Source { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public Task<RealmRuntime> Task => Source.Task;
            public int Admissions { get; set; }
            public RealmRuntime? PublishedRuntime { get; set; }
        }
    }
}
