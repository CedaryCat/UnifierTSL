using NuGet.Common;
using System.Collections.Immutable;
using UnifierTSL.Logging;
using UnifierTSL.Plugins.Hosts.Dotnet;

namespace UnifierTSL.PluginService.Loading
{
    public class PluginsLoadContext : ILoggerHost
    {
        private readonly Logger logCore;
        private readonly RoleLogger Logger;
        private readonly ImmutableArray<PluginContainer> Plugins;

        public string Name => "PluginInitializer";
        public string? CurrentLogCategory => null;

        internal PluginsLoadContext(Logger logger, IReadOnlyList<PluginContainer> plugins) {
            logCore = logger;
            Logger = UnifierApi.CreateLogger(this, logger);
            // Sort plugins by InitializationOrder, if same, sort by type name
            Plugins = [.. plugins
                .OrderBy(p => p.Plugin.InitializationOrder)
                .ThenBy(p => p.Plugin.GetType().Name, StringComparer.Ordinal)
            ];
        }

        internal PluginsLoadContext Initialize() {

            // Call BeforeGlobalInitialize method of all plugins
            foreach (var container in Plugins) {
                container.Plugin.BeforeGlobalInitialize(Plugins);
            }

            // Perform asynchronous initialization and wait for all initialization to complete
            InitializeAllAsync(Plugins).GetAwaiter().GetResult();

            return this;
        }

        async Task InitializeAllAsync(ImmutableArray<PluginContainer> sortedPlugins) {
            var initTasks = new Dictionary<PluginContainer, Task>();
            var initInfos = new PluginInitInfo[sortedPlugins.Length];

            for (int i = 0; i < sortedPlugins.Length; i++) {
                var current = sortedPlugins[i];

                // Extract the initialization information of the preceding plugins (in order)
                var prior = initInfos.AsMemory()[..i]; // Snapshot for ReadOnlyMemory

                // Create CancellationToken
                var token = CancellationToken.None;

                // Call InitializeAsync
                var task = InitializePlugin(Logger, current, prior, token);

                // Save Task and Plugin information
                initTasks[current] = task;
                initInfos[i] = new PluginInitInfo(current, task, token);
            }

            // Wait for all initialization tasks to complete
            await Task.WhenAll(initTasks.Values);

            if (initTasks.Count > 0) {
                Logger.Info(
                    message: $"All plugins ({initTasks.Count}) have been initialized.");
            }
            else {
                Logger.Info(
                    message: "No plugins have been initialized.");
            }
        }
        static async Task InitializePlugin(RoleLogger logger, PluginContainer container, ReadOnlyMemory<PluginInitInfo> prior, CancellationToken token) {
            await container.Plugin.InitializeAsync(prior, token);
            logger.Success($"Plugin {container.Name} v{container.Version} (by {container.Author}) initiated.");
        }
    }
}
