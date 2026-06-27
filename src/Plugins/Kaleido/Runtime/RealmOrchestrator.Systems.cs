using Kaleido.Systems;

namespace Kaleido.Runtime
{
    public sealed partial class RealmOrchestrator
    {
        public async Task<RealmSystemLease> MountSystemAsync(IRealmSystem system, CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(system);
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposalStarted) != 0, this);
            if (string.IsNullOrWhiteSpace(system.Id)) {
                throw new ArgumentException("Realm system id cannot be empty.", nameof(system));
            }

            var scope = new RealmSystemScope(this, system.Id);
            var lease = new RealmSystemLease(system, scope, ReleaseSystem);
            lock (registrationGate) {
                if (!mountedSystemIds.Add(system.Id)) {
                    throw new InvalidOperationException($"Realm system '{system.Id}' is already mounted.");
                }

                systemLeases.Add(lease);
            }

            try {
                await system.MountAsync(scope, cancellationToken).ConfigureAwait(false);
            }
            catch {
                await lease.DisposeAsync().ConfigureAwait(false);
                throw;
            }

            logger.Info(category: "System", message: $"Realm system '{system.Id}' mounted.");
            return lease;
        }

        internal IDisposable RegisterJoinHandler(RealmSystemScope scope, RealmJoinHandler handler, int priority) {
            var registration = new JoinRegistration(this, scope.SystemId, handler, priority, Interlocked.Increment(ref registrationSequence));
            lock (registrationGate) {
                joinHandlers.Add(registration);
            }

            return registration;
        }

        internal IDisposable RegisterMaintenance(RealmSystemScope scope, TimeSpan interval, RealmMaintenanceCallback callback) {
            var registration = new MaintenanceRegistration(this, scope.SystemId, interval, callback);
            lock (registrationGate) {
                maintenance.Add(registration);
            }

            return registration;
        }

        internal IDisposable RegisterInstanceRetiringHandler(RealmSystemScope scope, RealmEventHandler<RealmInstanceRetiring> handler) {
            var registration = new InstanceRetiringRegistration(this, scope.SystemId, handler, Interlocked.Increment(ref registrationSequence));
            lock (registrationGate) {
                instanceRetiringHandlers.Add(registration);
            }

            return registration;
        }

        internal void TickMaintenance(CancellationToken cancellationToken) {
            MaintenanceRegistration[] snapshot;
            lock (registrationGate) {
                snapshot = [.. maintenance];
            }

            var now = DateTimeOffset.UtcNow;
            foreach (var task in snapshot) {
                if (!task.TryBegin(now)) {
                    continue;
                }

                try {
                    var operation = RunMaintenanceAsync(task, cancellationToken);
                    activities.Track(operation);
                }
                catch (Exception ex) {
                    LogError("Maintenance", $"Realm system '{task.SystemId}' maintenance task failed.", ex);
                    task.End();
                }
            }
        }

        private async Task RunMaintenanceAsync(MaintenanceRegistration registration, CancellationToken cancellationToken) {
            try {
                await registration.InvokeAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            }
            catch (Exception ex) {
                LogError("Maintenance", $"Realm system '{registration.SystemId}' maintenance task failed.", ex);
            }
            finally {
                registration.End();
            }
        }

        private JoinRegistration[] GetJoinHandlersSnapshot() {
            lock (registrationGate) {
                return [.. joinHandlers
                    .OrderByDescending(static handler => handler.Priority)
                    .ThenBy(static handler => handler.Sequence)];
            }
        }

        private InstanceRetiringRegistration[] GetInstanceRetiringHandlersSnapshot() {
            lock (registrationGate) {
                return [.. instanceRetiringHandlers.OrderBy(static handler => handler.Sequence)];
            }
        }

        private void ReleaseSystem(RealmSystemLease lease) {
            lock (registrationGate) {
                systemLeases.Remove(lease);
                mountedSystemIds.Remove(lease.SystemId);
            }
        }

        internal void Unregister(JoinRegistration registration) {
            lock (registrationGate) {
                joinHandlers.Remove(registration);
            }
        }

        internal void Unregister(MaintenanceRegistration registration) {
            lock (registrationGate) {
                maintenance.Remove(registration);
            }
        }

        internal void Unregister(InstanceRetiringRegistration registration) {
            lock (registrationGate) {
                instanceRetiringHandlers.Remove(registration);
            }
        }
    }
}
