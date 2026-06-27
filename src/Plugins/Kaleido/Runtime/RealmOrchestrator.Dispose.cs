using Kaleido.Model.Lifecycle;

namespace Kaleido.Runtime
{
    public sealed partial class RealmOrchestrator
    {
        public ValueTask DisposeAsync() {
            if (Interlocked.Exchange(ref disposalStarted, 1) == 0) {
                _ = DisposeCoreAsync();
            }

            return new(disposed.Task);
        }

        private async Task DisposeCoreAsync() {
            try {
                joinSubscription.Dispose();
                transferSubscription.Dispose();
                leaveSubscription.Dispose();
                scheduler.Quiesce();
                lifetime.Cancel();
                await Registry.StopAsync().ConfigureAwait(false);
                await activities.DrainAsync().ConfigureAwait(false);
                await Task.WhenAll(Registry.Instances.Select(instance => RetireAsync(instance, RealmRetireReason.Shutdown))).ConfigureAwait(false);
                await retirements.DrainAsync().ConfigureAwait(false);
                foreach (var lease in systemLeases.ToArray()) {
                    await TryDisposeSystemAsync(lease).ConfigureAwait(false);
                }

                await scheduler.StopAsync().ConfigureAwait(false);
                foreach (var host in hosts) {
                    TryDisposeHost(host);
                }

                lifetime.Dispose();
                disposed.TrySetResult();
            }
            catch (Exception ex) {
                disposed.TrySetException(ex);
            }
        }

        private async Task TryDisposeSystemAsync(Systems.RealmSystemLease lease) {
            try {
                await lease.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex) {
                LogError("System", $"Realm system '{lease.SystemId}' disposal failed.", ex);
            }
        }

        private void TryDisposeHost(Hosting.IRealmHost host) {
            try {
                host.Dispose();
            }
            catch (Exception ex) {
                LogError("Realm", $"Realm host '{host.GetType().Name}' disposal failed.", ex);
            }
        }
    }
}
