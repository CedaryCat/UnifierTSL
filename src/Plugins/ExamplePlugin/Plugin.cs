using UnifierTSL.PluginHost.Hosts.Dotnet;
using UnifierTSL.Plugins;
using UnifierTSL.PluginService;

namespace ExamplePlugin
{
    [PluginMetadata("ExamplePlugin", "1.0.0", "Anonymous", "A test plugin for UnifierTSL")]
    public class Plugin : BasePlugin
    {
        public override async Task InitializeAsync(
            IPluginConfigRegistrar configRegistrar,
            ReadOnlyMemory<PluginInitInfo> priorInitializations,
            CancellationToken cancellationToken = default) {

            var configHandle = configRegistrar
                .CreateConfigRegistration<ExampleConfig>("configHandle.json", ConfigFormat.SystemTextJson)
                .WithDefault(() => new ExampleConfig { Name = "Example", Message = "Hello World!" })
                .OnDeserializationFailure(DeserializationFailureHandling.ReturnNewInstance)
                .OnSerializationFailure(SerializationFailureHandling.WriteNewInstance)
                .TriggerReloadOnExternalChange(true)
                .Complete();

            configHandle.OnChangedAsync += async (config) => {
                if (config is null) {
                    await Console.Out.WriteLineAsync($"Config set to null");
                }
                else {
                    await Console.Out.WriteLineAsync($"Config changed: {config.Name} {config.Message}");
                }
                return false;
            };

            var config = await configHandle.RequestAsync(cancellationToken: cancellationToken) ?? new ExampleConfig();

            await Console.Out.WriteLineAsync($"Config loaded: {config.Name} {config.Message}");
        }
    }
}
