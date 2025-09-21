namespace UnifierTSL.PluginHost
{
    public interface IPluginDiscoverer
    {
        /// <summary>
        /// Attempts to discover plugins in the specified folder using the given strategy. 
        /// If plugin information exists in the directory but does not meet the discovery strategy, 
        /// it will not be included in the returned list.
        /// </summary>
        /// <param name="pluginsDirectory">The path to the plugins directory.</param>
        /// <param name="discoveryMode">The discovery strategy to apply.</param>
        /// <returns>A read-only list of discovered plugin information.</returns>
        IReadOnlyList<IPluginInfo> DiscoverPlugins(string pluginsDirectory, PluginDiscoveryMode discoveryMode);

        /// <summary>
        /// Attempts to read plugin information from the specified file path using the given strategy.
        /// Returns true if plugin information is found in the file, regardless of whether it meets the discovery strategy.
        /// However, if the information does not meet the strategy, it will not be included in the <see cref="pluginInfo"/> list.
        /// </summary>
        /// <param name="pluginPath">The path to the plugin file.</param>
        /// <param name="discoveryMode">The discovery strategy to apply.</param>
        /// <param name="pluginInfo">The list of discovered plugin information, if any.</param>
        /// <returns>True if plugin information is found in the file; otherwise, false.</returns>
        bool TryDiscoverPlugin(string pluginPath, PluginDiscoveryMode discoveryMode, out IReadOnlyList<IPluginInfo> pluginInfo);
    }

    public enum PluginDiscoveryMode
    {
        /// <summary>
        /// Unconditionally read all plugin files.
        /// </summary>
        All,

        /// <summary>
        /// Only read updated or newly added plugin files.
        /// </summary>
        UpdatedOnly,

        /// <summary>
        /// Only read newly added plugin files.
        /// </summary>
        NewOnly
    }
}
