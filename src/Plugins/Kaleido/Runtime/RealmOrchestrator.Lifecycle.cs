using Kaleido.Model.Instances;
using Kaleido.Model.Lifecycle;
using Kaleido.Systems;
using UnifierTSL.Servers;

namespace Kaleido.Runtime
{
    public sealed partial class RealmOrchestrator
    {
        public void OnTransferred(ServerTransferNotification transfer) {
            var pending = TakePendingTransfer(transfer);
            if (pending is not null) {
                pending.Application = ApplyTransferAsync(
                    pending.PlayerId,
                    pending.SourceRuntime,
                    pending.TargetRuntime,
                    pending.TargetAdmission,
                    pending.Entry,
                    pending.Exit);
                return;
            }

            Registry.TryGetRuntime(transfer.Source, out var sourceRuntime);
            Registry.TryGetRuntime(transfer.Target, out var targetRuntime);
            activities.Track(ApplyTransferAsync(
                transfer.PlayerId,
                sourceRuntime,
                targetRuntime,
                null,
                Model.Transfer.RealmEntry.Default,
                Model.Transfer.RealmExit.Default));
        }

        public void OnLeft(ServerLeaveNotification leave) {
            if (Registry.TryGetRuntime(leave.Server, out var runtime)) {
                runtime.LeavePlayer(leave.PlayerId);
                activities.Track(DispatchAsync(runtime, () => TryDetach(runtime, leave.PlayerId, Model.Transfer.RealmExit.Default)));
            }
        }

        public async Task RetireAsync(RealmInstance instance, RealmRetireReason reason) {
            var runtime = instance.Runtime;
            if (runtime.TryRequestRetire(out var ready)) {
                StartRetirement(runtime, reason, ready);
            }

            await runtime.Removed.ConfigureAwait(false);
        }

        internal void StartRetirement(RealmRuntime runtime, RealmRetireReason reason, Task ready) {
            var operation = CompleteRetireWhenReadyAsync(runtime, reason, ready);
            retirements.Track(operation);
        }

        private async Task CompleteRetireWhenReadyAsync(RealmRuntime runtime, RealmRetireReason reason, Task ready) {
            await ready.ConfigureAwait(false);
            try {
                await InvokeInstanceRetiringAsync(runtime, reason).ConfigureAwait(false);
                var install = runtime.InstallScope ?? new Systems.Installation.RealmInstallScope(this, runtime);
                await UninstallAsync(runtime.Plan.Content, install).ConfigureAwait(false);
                TryDisposeInstall(install, runtime);
                await TryStopAsync(runtime, reason).ConfigureAwait(false);
                TryDisposeDriver(runtime);
            }
            catch (Exception ex) {
                LogError("Realm", $"Realm retirement failed unexpectedly for '{runtime.Plan.Key}'.", ex);
            }
            finally {
                if (!Registry.Remove(runtime)) {
                    runtime.MarkRemoved();
                }
            }
        }

        private async Task InvokeInstanceRetiringAsync(RealmRuntime runtime, RealmRetireReason reason) {
            var evt = new RealmInstanceRetiring(runtime.Instance, reason);
            foreach (var handler in GetInstanceRetiringHandlersSnapshot()) {
                try {
                    await handler.Handler(evt, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex) {
                    LogError("Realm", $"Realm system '{handler.SystemId}' instance-retiring handler failed for '{runtime.Plan.Key}'.", ex);
                }
            }
        }

        private void TryDisposeInstall(Systems.Installation.RealmInstallScope install, RealmRuntime runtime) {
            try {
                install.Dispose();
            }
            catch (Exception ex) {
                LogError("Realm", $"Realm install lifetime disposal failed for '{runtime.Plan.Key}'.", ex);
            }
        }
    }
}
