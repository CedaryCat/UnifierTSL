using System.Collections.Immutable;
using UnifierTSL.PluginService;

namespace UnifierTSL.Plugins
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
        /// An immutable array of all <see cref="IPluginContainer"/> instances loaded into the system.
        /// This enables a plugin to coordinate or hook into other plugins prior to any initialization logic.
        /// </param>
        void BeforeGlobalInitialize(ImmutableArray<IPluginContainer> plugins);

        /// <summary>
        /// Asynchronously initializes the plugin.
        /// 
        /// This method is called after the plugin is constructed. It receives a memory of 
        /// previously scheduled plugin initializations that occurred before this plugin 
        /// (ordered by <see cref="InitializationOrder"/>). 
        /// 
        /// The plugin may selectively await specific tasks from the memory if it depends on 
        /// certain plugins being initialized before continuing. If no such dependencies 
        /// exist, the plugin may proceed in parallel.
        /// </summary>
        /// <param name="configRegistrar">
        /// The registrar for plugin configuration files. 
        /// <para>
        /// Note: It is **not required** to register all configuration instances immediately 
        /// within <see cref="InitializeAsync"/>. A plugin may store this reference and 
        /// register configurations at any point during its lifetime. 
        /// Each registration returns an <see cref="IPluginConfigHandle{TConfig}"/> that
        /// provides host-defined access to the configuration file.
        /// </para>
        /// </param>
        /// <param name="priorInitializations">
        /// A read-only memory containing context information and initialization tasks 
        /// of plugins initialized earlier (lower InitializationOrder).
        /// </param>
        /// <param name="cancellationToken">Token for cooperative cancellation.</param>
        Task InitializeAsync(IPluginConfigRegistrar configRegistrar, ImmutableArray<PluginInitInfo> priorInitializations, CancellationToken cancellationToken);

        /// <summary>
        /// Called when the plugin is being shut down, typically during application shutdown or plugin reload.
        /// </summary>
        /// <param name="cancellationToken">Token for cooperative cancellation.</param>
        Task ShutdownAsync(CancellationToken cancellationToken);
    }
}
