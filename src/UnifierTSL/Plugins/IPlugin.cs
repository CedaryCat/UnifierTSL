using System.Collections.Immutable;
using UnifierTSL.Module;
using UnifierTSL.Plugins.Hosts.Dotnet;
using UnifierTSL.PluginService.Loading;

namespace UnifierTSL.PluginService
{
    /// <summary>
    /// Represents a plugin that can be initialized asynchronously with dependency awareness.
    /// </summary>
    public interface IPlugin : IAsyncDisposable
    {
        /// <summary>
        /// Gets the initialization order of the plugin. 
        /// Lower values indicate earlier initialization.
        /// If two plugins have the same initialization order, they will be initialized in the order of their type names.
        /// </summary>
        int InitializationOrder { get; }
        /// <summary>
        /// Called before the global initialization process begins for all plugins,
        /// after all plugin instances have been created and collected.
        /// 
        /// This allows the plugin to inspect and interact with other plugins, such as
        /// registering hooks, modifying behaviors, or setting up dependencies that
        /// must be in place before any plugin begins its asynchronous initialization.
        /// </summary>
        /// <param name="plugins">
        /// An immutable array of all <see cref="PluginContainer"/> instances loaded into the system.
        /// This enables a plugin to coordinate or hook into other plugins prior to any initialization logic.
        /// </param>
        void BeforeGlobalInitialize(ImmutableArray<PluginContainer> plugins);

        /// <summary>
        /// Asynchronously initializes the plugin.
        /// 
        /// This method is called after the plugin is constructed. It receives a span of 
        /// previously scheduled plugin initializations that occurred before this plugin 
        /// (ordered by <see cref="InitializationOrder"/>). 
        /// 
        /// The plugin may selectively await specific tasks from the span if it depends on 
        /// certain plugins being initialized before continuing. If no such dependencies 
        /// exist, the plugin may proceed in parallel.
        /// </summary>
        /// <param name="priorInitializations">
        /// A read-only span containing context information and initialization tasks 
        /// of plugins initialized earlier (lower InitializationOrder).
        /// </param>
        /// <param name="cancellationToken">Token for cooperative cancellation.</param>
        Task InitializeAsync(ReadOnlyMemory<PluginInitInfo> priorInitializations, CancellationToken cancellationToken);

        /// <summary>
        /// Called when the plugin is being shut down, typically during application shutdown or plugin reload.
        /// </summary>
        /// <param name="cancellationToken">Token for cooperative cancellation.</param>
        Task ShutdownAsync(CancellationToken cancellationToken);
    }
}
