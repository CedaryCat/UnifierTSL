namespace UnifierTSL.Plugins.Hosts.Dotnet
{
    /// <summary>
    /// Represents the context of a previously initialized plugin, including the plugin reference and its initialization task.
    /// </summary>
    public readonly struct PluginInitInfo(PluginContainer plugin, Task initializationTask, CancellationToken cancellationToken)
    {
        /// <summary>
        /// The plugin container instance for a previously initialized plugin.
        /// </summary>
        public readonly PluginContainer Plugin = plugin;

        /// <summary>
        /// The task representing the initialization of the plugin.
        /// </summary>
        public readonly Task InitializationTask = initializationTask;

        /// <summary>
        /// The cancellation token associated with the initialization task.
        /// </summary>
        public readonly CancellationToken CancellationToken = cancellationToken;
    }
}
