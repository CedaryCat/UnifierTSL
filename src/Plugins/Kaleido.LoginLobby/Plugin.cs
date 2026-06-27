using System.Collections.Immutable;
using Kaleido.Runtime;
using Kaleido.Systems;
using TShockAPI;
using UnifierTSL;
using UnifierTSL.Logging;
using UnifierTSL.Module;
using UnifierTSL.Plugins;

[assembly: RequiresCoreModule("Kaleido")]

namespace Kaleido.LoginLobby
{
    [PluginMetadata("Kaleido.LoginLobby", "1.0.0", "CedaryCat", "Reference private login lobby for Kaleido.")]
    public sealed class Plugin : BasePlugin, ILoggerHost
    {
        private IPluginConfigHandle<LoginLobbyConfig>? configHandle;
        private RealmSystemLease? systemLease;
        private LoginLobbyService? service;

        public static readonly int Order = Math.Max(global::Kaleido.Plugin.Order, TShockAPI.TShock.Order) + 1;

        public override int InitializationOrder => Order;
        public string Name => "Kaleido.LoginLobby";
        public string? CurrentLogCategory => null;
        public RoleLogger Logger { get; }
        public static LoginLobbyService Service { get; private set; } = null!;

        public Plugin() {
            Logger = UnifierApi.CreateLogger(this);
        }

        public override async Task InitializeAsync(
            IPluginConfigRegistrar configRegistrar,
            ImmutableArray<PluginInitInfo> priorInitializations,
            CancellationToken cancellationToken = default) {

            await priorInitializations.First(p => p.Plugin.Name == "Kaleido").InitializationTask.ConfigureAwait(false);
            var tshockInit = priorInitializations.FirstOrDefault(p => p.Plugin is TShockAPI.TShock || p.Plugin.Name == "TShock");
            if (tshockInit.Plugin is not null) {
                await tshockInit.InitializationTask.ConfigureAwait(false);
            }

            configRegistrar.DefaultOption
                .OnDeserializationFailure(DeserializationFailureHandling.ReturnNewInstance)
                .OnSerializationFailure(SerializationFailureHandling.WriteNewInstance)
                .TriggerReloadOnExternalChange(true);

            configHandle = configRegistrar
                .CreateConfigRegistration<LoginLobbyConfig>("config.json", ConfigFormat.SystemTextJson)
                .WithDefault(static () => new LoginLobbyConfig())
                .Complete();

            service = new LoginLobbyService(Logger);
            Service = service;
            systemLease = await global::Kaleido.Plugin.Orchestrator.MountSystemAsync(service, cancellationToken).ConfigureAwait(false);
            configHandle.OnChangedAsync += OnConfigChangedAsync;

            var config = await configHandle.RequestAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            service.UpdateConfig(config);
            if (tshockInit.Plugin is not null) {
                service.SetIdentityService(new TShockLoginLobbyIdentityService());
                Logger.Info("Kaleido login lobby is using the built-in TShock identity service.");
            }
            Logger.Info(config.Enabled
                ? "Kaleido login lobby loaded."
                : "Kaleido login lobby loaded but disabled by config.");
        }

        public override async Task ShutdownAsync(CancellationToken cancellationToken = default) {
            await DisposeServiceAsync().ConfigureAwait(false);
        }

        public override async ValueTask DisposeAsync(bool isDisposing) {
            await DisposeServiceAsync().ConfigureAwait(false);
            await base.DisposeAsync(isDisposing).ConfigureAwait(false);
        }

        private ValueTask<bool> OnConfigChangedAsync(IPluginConfigHandle<LoginLobbyConfig> handle, LoginLobbyConfig? config) {
            service?.UpdateConfig(config ?? new LoginLobbyConfig());
            return new(false);
        }

        private async ValueTask DisposeServiceAsync() {
            if (configHandle is not null) {
                configHandle.OnChangedAsync -= OnConfigChangedAsync;
                configHandle = null;
            }

            if (systemLease is not null) {
                await systemLease.DisposeAsync().ConfigureAwait(false);
            }
            systemLease = null;
            service = null;
        }
    }
}
