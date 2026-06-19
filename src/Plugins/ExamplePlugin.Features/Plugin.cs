using System.Collections.Immutable;
using UnifierTSL;
using UnifierTSL.Commanding.Composition;
using UnifierTSL.Logging;
using UnifierTSL.Module;
using UnifierTSL.Plugins;

[assembly: RequiresCoreModule(nameof(ExamplePlugin))]
namespace ExamplePlugin.Features
{
    [PluginMetadata("ExamplePlugin - Features", "1.0.0", "Anonymous", "A satellite example plugin.")]
    public class Plugin : BasePlugin, ILoggerHost
    {
        private IDisposable? commandingRegistration;

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
            commandingRegistration = CommandSystem.Install(static context =>
                context.EditCommands(static commands => {
                    var task = commands.Root(typeof(ExamplePlugin.ExampleSimulatedTaskCommand));
                    task.AddAlias("featuretask");
                    task.IfPathExists("legacy").Disable();
                    task.AddActionsFrom(typeof(ExampleFeatureTaskCommand));
                }));
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
