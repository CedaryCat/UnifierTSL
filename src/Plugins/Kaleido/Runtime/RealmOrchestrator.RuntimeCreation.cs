using Kaleido.Model.Hosting;
using Kaleido.Model.Ids;
using Kaleido.Model.Lifecycle;
using Kaleido.Model.Planning;
using Kaleido.Systems.Installation;

namespace Kaleido.Runtime
{
    public sealed partial class RealmOrchestrator
    {
        public async Task<Model.Instances.RealmInstance> EnsureAsync(RealmPlan plan, CancellationToken cancellationToken = default) {
            var runtime = await Registry.GetOrCreateAsync(plan, CreateRuntimeAsync, cancellationToken).ConfigureAwait(false);
            return runtime.Instance;
        }

        public async Task<Model.Transfer.RealmPreparation> PrepareAsync(RealmPlan plan, Model.Transfer.RealmPrepareOptions? options = null, CancellationToken cancellationToken = default) {
            options ??= Model.Transfer.RealmPrepareOptions.Default;
            if (options.HoldFor is { } holdFor && holdFor < TimeSpan.Zero) {
                throw new ArgumentOutOfRangeException(nameof(options), options.HoldFor, "Prepare hold duration cannot be negative.");
            }

            using var admission = await Registry.AcquireAsync(plan, CreateRuntimeAsync, cancellationToken).ConfigureAwait(false);
            var runtime = admission.Runtime;
            if (options.WaitUntilReady) {
                await runtime.Driver.WaitUntilReadyAsync(cancellationToken).ConfigureAwait(false);
            }

            return new(runtime.Instance, options.HoldFor.HasValue ? runtime.Hold(options.HoldFor.Value) : null);
        }

        private async Task<RealmRuntime> CreateRuntimeAsync(RealmPlan plan, CancellationToken cancellationToken) {
            var host = hosts.FirstOrDefault(host => host.CanHost(plan))
                ?? throw new InvalidOperationException($"No Kaleido host can satisfy realm '{plan.Key}'.");
            var id = new RealmInstanceId($"{plan.Key}:{Guid.NewGuid():N}");
            var session = await host.StartAsync(plan, this, id, cancellationToken).ConfigureAwait(false);
            var runtime = new RealmRuntime(this, id, plan, session, host.Capabilities);
            var install = new RealmInstallScope(this, runtime);
            runtime.SetInstallScope(install);
            List<IRealmContentInstaller> installed = [];

            try {
                foreach (var installer in plan.Content) {
                    await installer.InstallAsync(install, cancellationToken).ConfigureAwait(false);
                    installed.Add(installer);
                }
            }
            catch (Exception ex) {
                LogError("Realm", $"Realm '{plan.Key}' content installation failed. Cleaning up the started host.", ex);
                await CleanupFailedStartAsync(runtime, installed).ConfigureAwait(false);
                throw;
            }

            logger.Info(category: "Realm", message: $"Realm '{plan.Key}' started on {host.Capabilities.Kind}.");
            return runtime;
        }

        private async Task CleanupFailedStartAsync(RealmRuntime runtime, IReadOnlyList<IRealmContentInstaller> installed) {

            runtime.TryRequestRetire(out _);
            var install = runtime.InstallScope!;
            await UninstallAsync(installed, install).ConfigureAwait(false);
            install.Dispose();
            await TryStopAsync(runtime, new(RealmRetireKind.Failed, "Realm startup failed.")).ConfigureAwait(false);
            TryDisposeDriver(runtime);
        }

        private async Task UninstallAsync(IReadOnlyList<IRealmContentInstaller> installers, RealmInstallScope install) {
            for (int i = installers.Count - 1; i >= 0; i--) {
                await TryUninstallAsync(installers[i], install).ConfigureAwait(false);
            }
        }

        private async Task TryUninstallAsync(IRealmContentInstaller installer, RealmInstallScope install) {
            try {
                await installer.UninstallAsync(install).ConfigureAwait(false);
            }
            catch (Exception ex) {
                LogError("Realm", $"Realm content uninstall failed for '{install.Instance.Plan.Key}'.", ex);
            }
        }

        private async Task TryStopAsync(RealmRuntime runtime, RealmRetireReason reason) {
            try {
                await runtime.Driver.StopAsync(reason).ConfigureAwait(false);
            }
            catch (Exception ex) {
                LogError("Realm", $"Realm host stop failed for '{runtime.Plan.Key}'.", ex);
            }
        }

        private void TryDisposeDriver(RealmRuntime runtime) {
            try {
                runtime.Driver.Dispose();
            }
            catch (Exception ex) {
                LogError("Realm", $"Realm host dispose failed for '{runtime.Plan.Key}'.", ex);
            }
        }
    }
}
