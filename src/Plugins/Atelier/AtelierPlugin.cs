using Atelier.Commanding;
using Atelier.Presentation.Window;
using Atelier.Presentation.Window.Formatting;
using Atelier.Session;
using Atelier.Session.Roslyn;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using UnifierTSL;
using UnifierTSL.Commanding.Composition;
using UnifierTSL.Module;
using UnifierTSL.Plugins;

[assembly: CoreModule]

namespace Atelier
{
    [PluginMetadata("Atelier", "1.0.0", "Anonymous", "Minimal Atelier authoring window plugin.")]
    public sealed class AtelierPlugin : BasePlugin
    {
        private IDisposable? commandingRegistration;
        private IPluginConfigHandle<AtelierConfig>? configHandle;
        private AtelierConfig config = new();
        private ReplManager? replManager;

        internal static ReplWindowService? WindowService { get; private set; }

        public string Name => "Atelier";

        public override async Task InitializeAsync(
            IPluginConfigRegistrar configRegistrar,
            ImmutableArray<PluginInitInfo> priorInitializations,
            CancellationToken cancellationToken = default) {
            configRegistrar.DefaultOption
                .OnDeserializationFailure(DeserializationFailureHandling.ReturnNewInstance)
                .OnSerializationFailure(SerializationFailureHandling.WriteNewInstance)
                .TriggerReloadOnExternalChange(true);

            configHandle = configRegistrar
                .CreateConfigRegistration<AtelierConfig>("config.json", ConfigFormat.SystemTextJson)
                .WithDefault(static () => new AtelierConfig())
                .Complete();
            configHandle.OnChangedAsync += OnConfigChangedAsync;
            config = await configHandle.RequestAsync(cancellationToken: cancellationToken).ConfigureAwait(false)
                ?? new AtelierConfig();

            RoslynHost.Initialize();
            RoslynFormattingWorkspace.Initialize();
            replManager = new ReplManager(() => config);
            replManager.StartWarmup();
            WindowService = new ReplWindowService(replManager);
            commandingRegistration = CommandSystem.Install(static context =>
                context.AddControllerGroup<TerminalCommandController>());
        }

        public override Task ShutdownAsync(CancellationToken cancellationToken = default) {
            UnregisterRuntimeBindings();
            return Task.CompletedTask;
        }

        public override ValueTask DisposeAsync(bool isDisposing) {
            UnregisterRuntimeBindings();
            return base.DisposeAsync(isDisposing);
        }

        private void UnregisterRuntimeBindings() {
            commandingRegistration?.Dispose();
            commandingRegistration = null;
            if (configHandle is not null) {
                configHandle.OnChangedAsync -= OnConfigChangedAsync;
                configHandle.Dispose();
                configHandle = null;
            }

            WindowService?.Dispose();
            WindowService = null;
            replManager?.Dispose();
            replManager = null;
            RoslynFormattingWorkspace.Dispose();
        }

        private ValueTask<bool> OnConfigChangedAsync(IPluginConfigHandle<AtelierConfig> handle, AtelierConfig? config) {
            this.config = config ?? new AtelierConfig();
            return ValueTask.FromResult(false);
        }
    }
}
