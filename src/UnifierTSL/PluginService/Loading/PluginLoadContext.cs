using System.Collections.Immutable;

namespace UnifierTSL.PluginService.Loading
{
    public class PluginsLoadContext
    {
        private readonly ImmutableArray<PluginContainer> Plugins;
        internal PluginsLoadContext(IReadOnlyList<PluginContainer> plugins) {
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

        static async Task InitializeAllAsync(ImmutableArray<PluginContainer> sortedPlugins) {
            var initTasks = new Dictionary<PluginContainer, Task>();
            var initInfos = new PluginInitInfo[sortedPlugins.Length];

            for (int i = 0; i < sortedPlugins.Length; i++) {
                var current = sortedPlugins[i];

                // Extract the initialization information of the preceding plugins (in order)
                var prior = initInfos.AsSpan()[..i]; // Snapshot for ReadOnlySpan
                var plugin = current.Plugin;

                // Create CancellationToken
                var token = CancellationToken.None;

                // Call InitializeAsync
                var task = plugin.InitializeAsync(prior, token);

                // Save Task and Plugin information
                initTasks[current] = task;
                initInfos[i] = new PluginInitInfo(current, task, token);
            }

            // Wait for all initialization tasks to complete
            await Task.WhenAll(initTasks.Values);
        }
    }

}
