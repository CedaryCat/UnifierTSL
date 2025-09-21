using UnifierTSL.Logging;
using UnifierTSL.PluginService;

namespace UnifierTSL.PluginHost
{
    public interface IPluginHost : IKeySelector<string>, ILoggerHost
    {
        IReadOnlyList<IPluginContainer> Plugins { get; }
        IPluginDiscoverer PluginDiscoverer { get; }
        IPluginLoader PluginLoader { get; }

        Task InitializePluginsAsync(CancellationToken cancellationToken = default);
        Task UnloadPluginsAsync(CancellationToken cancellationToken = default);
        Task ShutdownAsync(CancellationToken cancellationToken = default);
    }
}
