using System.Collections.Immutable;
using Kaleido.Context;
using Kaleido.Runtime;
using UnifierTSL;
using UnifierTSL.Logging;
using UnifierTSL.Module;
using UnifierTSL.Plugins;

[assembly: CoreModule]

namespace Kaleido
{
    [PluginMetadata("Kaleido", "1.0.0", "CedaryCat", "Official realm framework for UnifierTSL.")]
    public sealed class Plugin : BasePlugin, ILoggerHost
    {
        private RealmOrchestrator? orchestrator;

        public const int Order = 7;

        public override int InitializationOrder => Order;
        public string Name => "Kaleido";
        public string? CurrentLogCategory => null;

        public RoleLogger Logger { get; }
        public static RealmOrchestrator Orchestrator { get; private set; } = null!;

        public Plugin() {
            Logger = UnifierApi.CreateLogger(this);
        }

        public override Task InitializeAsync(
            IPluginConfigRegistrar configRegistrar,
            ImmutableArray<PluginInitInfo> priorInitializations,
            CancellationToken cancellationToken = default) {

            RealmExtensionsBootstrap.EnsureInitialized();
            orchestrator = new(Logger);
            Orchestrator = orchestrator;
            Logger.Info("Kaleido realm orchestrator initialized.");
            return Task.CompletedTask;
        }

        public override async Task ShutdownAsync(CancellationToken cancellationToken = default) {
            await DisposeOrchestratorAsync().ConfigureAwait(false);
        }

        public override async ValueTask DisposeAsync(bool isDisposing) {
            await DisposeOrchestratorAsync().ConfigureAwait(false);
            await base.DisposeAsync(isDisposing).ConfigureAwait(false);
        }

        private async ValueTask DisposeOrchestratorAsync() {
            if (orchestrator is null) {
                return;
            }

            await orchestrator.DisposeAsync().ConfigureAwait(false);
            orchestrator = null;
            Orchestrator = null!;
        }
    }
}
