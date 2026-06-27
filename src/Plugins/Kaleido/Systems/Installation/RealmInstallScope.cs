using Kaleido.Hosting.SharedProjection;
using Kaleido.Model.Instances;
using Kaleido.Runtime;
using UnifierTSL.Servers;

namespace Kaleido.Systems.Installation
{
    public sealed class RealmInstallScope : IDisposable
    {
        private readonly List<IDisposable> registrations = [];
        private readonly List<Action<RealmPlayerEntering>> entering = [];
        private readonly List<Action<RealmPlayerLeaving>> leaving = [];
        private readonly Lock gate = new();
        private bool disposed;

        internal RealmInstallScope(RealmOrchestrator orchestrator, RealmRuntime runtime) {
            Instance = runtime.Instance;
            Server = runtime.Server;
            Realms = new(orchestrator);
            Lifetime = new(this);
            Runtime = new(this);
            Transfers = new(this);
            Projection = runtime.Server is SharedProjectionContext projection ? new(this, projection) : null;
        }

        public RealmInstance Instance { get; }
        public ServerContext Server { get; }
        public RealmSystemRealms Realms { get; }
        public RealmInstallLifetime Lifetime { get; }
        public RealmRuntimeHooks Runtime { get; }
        public RealmTransferHooks Transfers { get; }
        public RealmProjectionHooks? Projection { get; }

        internal IDisposable Track(IDisposable registration) {
            ArgumentNullException.ThrowIfNull(registration);
            lock (gate) {
                if (disposed) {
                    registration.Dispose();
                    throw new ObjectDisposedException(nameof(RealmInstallScope));
                }

                registrations.Add(registration);
                return registration;
            }
        }

        internal IDisposable RegisterEntering(Action<RealmPlayerEntering> handler) {
            lock (gate) {
                ThrowIfDisposed();
                entering.Add(handler);
                var registration = new EventRegistration(() => RemoveEntering(handler));
                registrations.Add(registration);
                return registration;
            }
        }

        internal IDisposable RegisterLeaving(Action<RealmPlayerLeaving> handler) {
            lock (gate) {
                ThrowIfDisposed();
                leaving.Add(handler);
                var registration = new EventRegistration(() => RemoveLeaving(handler));
                registrations.Add(registration);
                return registration;
            }
        }

        internal void InvokeEntering(RealmPlayerEntering evt, Action<Exception> onError) {
            Action<RealmPlayerEntering>[] snapshot;
            lock (gate) {
                snapshot = [.. entering];
            }

            foreach (var handler in snapshot) {
                try {
                    handler(evt);
                }
                catch (Exception ex) {
                    onError(ex);
                }
            }
        }

        internal void InvokeLeaving(RealmPlayerLeaving evt, Action<Exception> onError) {
            Action<RealmPlayerLeaving>[] snapshot;
            lock (gate) {
                snapshot = [.. leaving];
            }

            foreach (var handler in snapshot) {
                try {
                    handler(evt);
                }
                catch (Exception ex) {
                    onError(ex);
                }
            }
        }

        private void RemoveEntering(Action<RealmPlayerEntering> handler) {
            lock (gate) {
                entering.Remove(handler);
            }
        }

        private void RemoveLeaving(Action<RealmPlayerLeaving> handler) {
            lock (gate) {
                leaving.Remove(handler);
            }
        }

        private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

        public void Dispose() {
            List<IDisposable> snapshot;
            lock (gate) {
                if (disposed) {
                    return;
                }

                disposed = true;
                snapshot = [.. registrations];
                registrations.Clear();
                entering.Clear();
                leaving.Clear();
            }

            for (int i = snapshot.Count - 1; i >= 0; i--) {
                snapshot[i].Dispose();
            }
        }
    }
}
