using UnifierTSL.Logging;
using UnifierTSL.PluginService;

namespace UnifierTSL.PluginHost
{
    /// <summary>
    /// Defines a plugin host implementation (for example, the built-in .NET host).
    /// Host-level lifecycle methods are intended for the whole host, not targeted single-plugin hot reload.
    /// Use <see cref="IPluginLoader"/> for targeted load/unload operations.
    /// </summary>
    public interface IPluginHost : IKeySelector<string>, ILoggerHost
    {
        IReadOnlyList<IPluginContainer> Plugins { get; }
        IPluginDiscoverer PluginDiscoverer { get; }
        IPluginLoader PluginLoader { get; }

        /// <summary>
        /// Discovers and initializes plugins managed by this host.
        /// </summary>
        Task InitializePluginsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Unloads plugin containers managed by this host.
        /// This removes modules from the host/runtime load context, but does not imply graceful shutdown callbacks.
        /// Call <see cref="ShutdownAsync(CancellationToken)"/> first when graceful teardown is required.
        /// </summary>
        Task UnloadPluginsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Performs graceful shutdown callbacks for plugins managed by this host (for example plugin background workers).
        /// This does not unload assemblies by itself.
        /// </summary>
        Task ShutdownAsync(CancellationToken cancellationToken = default);
    }
}
