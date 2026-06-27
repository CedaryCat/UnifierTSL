using Kaleido.Runtime;
using UnifierTSL.Servers;

namespace Kaleido.Hosting.SharedProjection
{
    internal sealed class ProjectionDispatcher(global::UnifierTSL.Servers.ServerContext server) : QueuedServerDispatcher(server)
    {
        // Projection dispatchers share one scheduler thread, so access is identified by the active logical projection.
        // A bounded drain prevents one projection's queue from starving every other projection and lifecycle task.
        private const int MaxWorkItemsPerFrame = 256;
        [ThreadStatic]
        private static ProjectionDispatcher? current;
        private readonly Lock executionGate = new();
        private IDisposable? registration;
        private ServerDispatchDomain? domain;
        private int bindingStarted;
        private int bound;

        public override bool CheckAccess() => !IsDisposed && ReferenceEquals(current, this);
        public override ServerDispatchDomain Domain => domain
            ?? throw new InvalidOperationException($"Projection dispatcher for '{Server.Name}' is not bound to a running scheduler.");

        internal void Bind(IDispatchScheduler scheduler) {
            ArgumentNullException.ThrowIfNull(scheduler);
            if (Interlocked.Exchange(ref bindingStarted, 1) != 0) {
                throw new InvalidOperationException($"Projection dispatcher for '{Server.Name}' is already bound.");
            }

            Volatile.Write(ref bound, 1);
            domain = scheduler.Domain;
            try {
                registration = scheduler.Register(
                    string.IsNullOrWhiteSpace(Server.Name) ? GetType().Name : Server.Name,
                    Drain,
                    OnSchedulerStopped);
            }
            catch {
                Volatile.Write(ref bound, 0);
                domain = null;
                throw;
            }
        }

        internal void Run(Action action) {
            ArgumentNullException.ThrowIfNull(action);
            lock (executionGate) {
                EnsureAvailable();
                var previous = current;
                try {
                    current = this;
                    RunInDispatchContext(() => {
                        DrainQueue(MaxWorkItemsPerFrame);
                        action();
                    });
                }
                finally {
                    current = previous;
                }
            }
        }

        protected override void EnsureTargetAvailable() {
            if (Volatile.Read(ref bound) == 0) {
                throw new InvalidOperationException($"Projection dispatcher for '{Server.Name}' is not bound to a running scheduler.");
            }
        }

        private void Drain() {
            lock (executionGate) {
                var previous = current;
                try {
                    current = this;
                    DrainQueue(MaxWorkItemsPerFrame);
                }
                finally {
                    current = previous;
                }
            }
        }

        private void OnSchedulerStopped() {
            Volatile.Write(ref bound, 0);
            RejectPending(new ObjectDisposedException(nameof(RealmScheduler)));
        }

        public override void Dispose() {
            lock (executionGate) {
                if (!TryDisposeQueue()) {
                    return;
                }

                Volatile.Write(ref bound, 0);
                domain = null;
                Interlocked.Exchange(ref registration, null)?.Dispose();
            }
        }
    }
}
