using System.Collections.Immutable;
using UnifierTSL;
using UnifierTSL.Logging;
using UnifierTSL.Module;
using UnifierTSL.Plugins;

[assembly: RequiresCoreModule(nameof(ExamplePlugin))]
namespace ExamplePlugin.Features
{
    [PluginMetadata("ExamplePlugin - Features", "1.0.0", "Anonymous", "A satellite example plugin.")]
    public class Plugin : BasePlugin, ILoggerHost
    {
        public string Name => "ExamplePlugin - Features";
        public string? CurrentLogCategory => null;
        public override int InitializationOrder => ExamplePlugin.Plugin.Order + 1;

        readonly RoleLogger logger;
        public Plugin() {
            logger = UnifierApi.CreateLogger(this);
        }

        public override async Task InitializeAsync(
            IPluginConfigRegistrar configRegistrar,
            ImmutableArray<PluginInitInfo> priors,
            CancellationToken cancellationToken = default) {

            await priors.First(p => p.Plugin.Name == nameof(ExamplePlugin)).InitializationTask;

            ExampleTool.DoSomething(logger);
        }
    }
}
