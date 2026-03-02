using System.Collections.Immutable;
using UnifierTSL.Logging;
using UnifierTSL.PluginService;

namespace UnifierTSL.PluginHost.Hosts.Dotnet
{
    public partial class DotnetPluginHost : IPluginHost
    {
        public ImmutableArray<PluginContainer> Plugins = [];
        public RoleLogger Logger { get; init; }

        public string Name => "UTSL-PluginHost";
        public string Key => "dotnet";
        public string? CurrentLogCategory => null;

        IReadOnlyList<IPluginContainer> IPluginHost.Plugins => Plugins;
        public IPluginDiscoverer PluginDiscoverer { get; init; }
        public IPluginLoader PluginLoader { get; init; }
        public DotnetPluginHost() {
            Logger = UnifierApi.CreateLogger(this);
            PluginDiscoverer = new PluginDiscoverer(this);
            PluginLoader = new PluginLoader(this);
        }

        public async Task ShutdownAsync(CancellationToken cancellationToken = default) {
            ImmutableArray<IPluginContainer> plugins = SortPlugins();

            for (int i = plugins.Length - 1; i >= 0; i--) {
                cancellationToken.ThrowIfCancellationRequested();

                PluginContainer container = (PluginContainer)plugins[i];
                if (container.LoadStatus is not PluginLoadStatus.Loaded) {
                    continue;
                }

                try {
                    await container.Plugin.ShutdownAsync(cancellationToken);
                    Logger.InfoWithMetadata(
                        category: "Shutdown",
                        message: GetParticularString("{0} is plugin name", $"Plugin '{container.Name}' shutdown completed."),
                        metadata: [new("PluginFile", container.Location.FilePath)]);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                    throw;
                }
                catch (Exception ex) {
                    Logger.LogHandledExceptionWithMetadata(
                        category: "Shutdown",
                        message: GetParticularString("{0} is plugin name, {1} is error message", $"Plugin '{container.Name}' failed to shutdown: {ex.Message}"),
                        metadata: [new("PluginFile", container.Location.FilePath)],
                        ex: ex);
                }
            }
        }

        public async Task UnloadPluginsAsync(CancellationToken cancellationToken = default) {
            ImmutableArray<IPluginContainer> plugins = SortPlugins();

            for (int i = plugins.Length - 1; i >= 0; i--) {
                cancellationToken.ThrowIfCancellationRequested();

                PluginContainer container = (PluginContainer)plugins[i];
                if (container.LoadStatus is PluginLoadStatus.Unloaded || container.Module.Unloaded) {
                    continue;
                }

                try {
                    PluginLoader.ForceUnloadPlugin(container);
                    Logger.InfoWithMetadata(
                        category: "Unloading",
                        message: GetParticularString("{0} is plugin name", $"Plugin '{container.Name}' unload completed."),
                        metadata: [new("PluginFile", container.Location.FilePath)]);
                }
                catch (Exception ex) {
                    Logger.LogHandledExceptionWithMetadata(
                        category: "Unloading",
                        message: GetParticularString("{0} is plugin name, {1} is error message", $"Plugin '{container.Name}' failed to unload: {ex.Message}"),
                        metadata: [new("PluginFile", container.Location.FilePath)],
                        ex: ex);
                }
            }
        }
    }
}
