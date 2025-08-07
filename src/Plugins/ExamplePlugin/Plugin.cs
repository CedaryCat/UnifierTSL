using System.Collections.Immutable;
using UnifierTSL;
using UnifierTSL.Logging;
using UnifierTSL.Plugins;

namespace ExamplePlugin
{
    [PluginMetadata("ExamplePlugin", "1.0.0", "Anonymous", "A test plugin for UnifierTSL")]
    public class Plugin : BasePlugin, ILoggerHost
    {
        public string Name => "ExamplePlugin";
        public string? CurrentLogCategory => null;

        readonly RoleLogger logger;
        public Plugin() {
            logger = UnifierApi.CreateLogger(this);
        }

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

            configHandle.OnChangedAsync += (config) => {
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
        }
    }
}
