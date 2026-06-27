using Kaleido.Runtime;

namespace Kaleido.Systems
{
    public sealed class RealmSystemScope : IDisposable
    {
        private readonly RealmOrchestrator orchestrator;
        private readonly List<IDisposable> registrations = [];
        private readonly Lock gate = new();
        private bool disposed;

        internal RealmSystemScope(RealmOrchestrator orchestrator, string systemId) {
            this.orchestrator = orchestrator;
            SystemId = systemId;
            Join = new(this);
            Realms = new(orchestrator);
            Maintenance = new(this);
            Events = new(this);
        }

        public string SystemId { get; }
        public RealmJoinPipeline Join { get; }
        public RealmSystemRealms Realms { get; }
        public RealmSystemMaintenance Maintenance { get; }
        public RealmSystemEvents Events { get; }

        internal IDisposable RegisterJoinHandler(RealmJoinHandler handler, int priority) {
            lock (gate) {
                ThrowIfDisposed();
                var registration = orchestrator.RegisterJoinHandler(this, handler, priority);
                registrations.Add(registration);
                return registration;
            }
        }

        internal IDisposable RegisterMaintenance(TimeSpan interval, RealmMaintenanceCallback callback) {
            lock (gate) {
                ThrowIfDisposed();
                var registration = orchestrator.RegisterMaintenance(this, interval, callback);
                registrations.Add(registration);
                return registration;
            }
        }

        internal IDisposable RegisterInstanceRetiringHandler(RealmEventHandler<RealmInstanceRetiring> handler) {
            lock (gate) {
                ThrowIfDisposed();
                var registration = orchestrator.RegisterInstanceRetiringHandler(this, handler);
                registrations.Add(registration);
                return registration;
            }
        }

        internal void ThrowIfDisposed() {
            if (disposed) {
                throw new ObjectDisposedException(nameof(RealmSystemScope));
            }
        }

        public void Dispose() {
            List<IDisposable> snapshot;
            lock (gate) {
                if (disposed) {
                    return;
                }

                disposed = true;
                snapshot = [.. registrations];
                registrations.Clear();
            }

            for (int i = snapshot.Count - 1; i >= 0; i--) {
                snapshot[i].Dispose();
            }
        }
    }
}
