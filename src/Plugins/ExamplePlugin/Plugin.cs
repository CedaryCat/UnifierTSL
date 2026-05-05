using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using UnifierTSL;
using UnifierTSL.Commanding.Composition;
using UnifierTSL.Logging;
using UnifierTSL.Module;
using UnifierTSL.Plugins;

[assembly: CoreModule]
[assembly: InternalsVisibleTo("ExamplePlugin.Features")]

namespace ExamplePlugin
{
    [PluginMetadata("ExamplePlugin", "1.0.0", "Anonymous", "A test plugin for UnifierTSL")]
    public class Plugin : BasePlugin, ILoggerHost
    {
        private IDisposable? commandingRegistration;

        public string Name => "ExamplePlugin";
        public string? CurrentLogCategory => null;

        readonly RoleLogger logger;
        public Plugin() {
            logger = UnifierApi.CreateLogger(this);
        }
        internal const int Order = 6;
        public override int InitializationOrder => Order;
        public override async Task InitializeAsync(
            IPluginConfigRegistrar configRegistrar,
            ImmutableArray<PluginInitInfo> priorInitializations,
            CancellationToken cancellationToken = default) {

            configRegistrar.DefaultOption
                .OnDeserializationFailure(DeserializationFailureHandling.ReturnNewInstance)
                .OnSerializationFailure(SerializationFailureHandling.WriteNewInstance)
                .TriggerReloadOnExternalChange(true);

            var configHandle = configRegistrar
                .CreateConfigRegistration<ExampleConfig>("config.json", ConfigFormat.SystemTextJson)
                .WithDefault(() => new ExampleConfig { Name = "Example", Message = "Hello World!" })
                .Complete();

            configHandle.OnChangedAsync += (handle, config) => {
                if (config is null) {
                    logger.Info("Config set to null");
                }
                else {
                    logger.Info($"Config changed: {config.Name} {config.Message}");
                }
                return new ValueTask<bool>(false);
            };

            var config = await configHandle.RequestAsync(cancellationToken: cancellationToken);

            logger.Info($"Config loaded: {config.Name} {config.Message}");
            commandingRegistration = CommandSystem.Install(static context =>
                context.AddControllerGroup<ExampleTerminalCommandController>());
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
        }
    }
}
