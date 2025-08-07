using System.Collections.Immutable;
using UnifierTSL.Logging;
using UnifierTSL.PluginHost.Configs;
using UnifierTSL.Plugins;
using UnifierTSL.PluginService;

namespace UnifierTSL.PluginHost.Hosts.Dotnet
{
    partial class DotnetPluginHost
    {
        ImmutableArray<IPluginContainer> SortPlugins() {
			return [.. Plugins
				.OrderBy(p => p.Plugin.InitializationOrder)
				.ThenBy(p => p.Plugin.GetType().Name, StringComparer.Ordinal)
			];
		}

		public async Task InitializePluginsAsync(CancellationToken cancellationToken = default) {

#warning TODO Unload old plugins?
			var infos = PluginDiscoverer.DiscoverPlugins("plugins", PluginDiscoveryMode.UpdatedOnly);

            foreach (var info in infos) {
                PluginLoader.LoadPlugin(info);
            }

			var plugins = SortPlugins();
			
			// Call BeforeGlobalInitialize method of all plugins
			foreach (var container in plugins) {
				container.Plugin.BeforeGlobalInitialize(plugins);
			}

			// Perform asynchronous initialization and wait for all initialization to complete
			await InitializeAllAsync(Logger, plugins, cancellationToken);
		}
		static async Task InitializeAllAsync(RoleLogger logger, ImmutableArray<IPluginContainer> sortedPlugins, CancellationToken token) {
			var initTasks = new Dictionary<PluginContainer, Task>();
			var initInfos = new PluginInitInfo[sortedPlugins.Length];

			for (int i = 0; i < sortedPlugins.Length; i++) {
				var current = (PluginContainer)sortedPlugins[i];

				// Extract the initialization information of the preceding plugins (in order)
				var prior = initInfos[..i]; // Snapshot for ReadOnlyMemory

				// Call InitializeAsync
				var task = InitializePlugin(logger, current, [.. prior], token);

				// Save Task and Plugins information
				initTasks[current] = task;
				initInfos[i] = new PluginInitInfo(current, task, token);
			}

			// Wait for all initialization tasks to complete
			await Task.WhenAll(initTasks.Values);

			if (initTasks.Count > 0) {
				logger.Info(
					message: $"All plugins ({initTasks.Count}) have been initialized.");
			}
			else {
				logger.Info(
					message: "No plugins have been initialized.");
			}
		}

		static async Task InitializePlugin(RoleLogger logger, PluginContainer container, ImmutableArray<PluginInitInfo> prior, CancellationToken token) {
			if (container.Status is PluginStatus.Disabled) {
				logger.InfoWithMetadata(
					category: "Init",
					message: $"Plugins {container.Name} is disabled, Skipped.",
					metadata: [new("PluginFile", container.Module.Signature.FilePath)]);
				return;
			}

			if (container.LoadStatus is not PluginLoadStatus.NotLoaded) {
				logger.InfoWithMetadata(
					category: "Init",
					message: $"Plugins {container.Name} in Status {container.LoadStatus}, Skipped.",
					metadata: [new("PluginFile", container.Module.Signature.FilePath)]);
				return;
			}

			try {
				var config = new ConfigRegistrar(container, Path.Combine("config", Path.GetFileNameWithoutExtension(container.Location.FilePath)));
				await container.Plugin.InitializeAsync(config, prior, token);
				container.LoadStatus = PluginLoadStatus.Loaded;
				logger.Success($"Plugins {container.Name} v{container.Version} (by {container.Author}) initiated.");
			}
			catch (Exception ex) {
				container.LoadStatus = PluginLoadStatus.Failed;
				container.LoadError = ex;

				logger.LogHandledExceptionWithMetadata(
					category: "Init",
					message: $"Plugins {container.Name} failed to initialize: {ex.Message}",
					metadata: [new("PluginFile", container.Module.Signature.FilePath)],
					ex: ex);
			}
		}
	}
}
