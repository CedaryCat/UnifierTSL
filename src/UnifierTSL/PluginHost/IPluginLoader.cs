using UnifierTSL.PluginService;

namespace UnifierTSL.PluginHost
{
    public interface IPluginLoader
    {
        /// <summary>
        /// Attempts to load a plugin from the given <paramref name="pluginInfo"/> descriptor.
        /// </summary>
        /// <param name="pluginInfo">
        /// Metadata describing the plugin to be loaded, typically extracted by a plugin sniffer.
        /// </param>
        /// <param name="loadDetails">
        /// Output value describing the result of the load attempt. Possible values:
        /// <list type="bullet">
        /// <item>
        /// <term><see cref="LoadDetails.Success"/></term>
        /// <description>The plugin was successfully loaded.</description>
        /// </item>
        /// <item>
        /// <term><see cref="LoadDetails.AlreadyLoaded"/></term>
        /// <description>
        /// A plugin with the same <c>FileSignature</c> is already loaded in the <c>IPluginHost</c>.  
        /// The existing instance is returned without loading a duplicate.
        /// </description>
        /// </item>
        /// <item>
        /// <term><see cref="LoadDetails.ExistingOldVersion"/></term>
        /// <description>
        /// A plugin already exists in the same location but with a different <c>FileSignature</c>.  
        /// The old plugin must be unloaded before loading the new version.  
        /// No plugin is loaded in this case, and <c>null</c> is returned.
        /// </description>
        /// </item>
        /// <item>
        /// <term><see cref="LoadDetails.Failed"/></term>
        /// <description>Loading failed due to an error.</description>
        /// </item>
        /// </list>
        /// </param>
        /// <returns>
        /// The loaded <see cref="IPluginContainer"/> instance if successful or already loaded; otherwise, <c>null</c>.
        /// </returns>
        IPluginContainer? LoadPlugin(IPluginInfo pluginInfo, out LoadDetails loadDetails);

        /// <summary>
        /// Forcefully unloads the specified plugin and all of its dependent plugins, 
        /// resolving dependencies before removal.
        /// </summary>
        /// <param name="pluginContainer">
        /// The plugin container to unload.
        /// </param>
        /// <remarks>
        /// This operation will:
        /// <list type="bullet">
        /// <item>Identify all plugins that depend on the target plugin.</item>
        /// <item>Unload the entire dependency chain in the correct order.</item>
        /// </list>
        /// Unloading only guarantees removal from the <c>IPluginHost</c>.  
        /// Actual resource cleanup depends on both the host and the plugin’s own implementation 
        /// (e.g., in .NET, the host might unload the corresponding <c>AssemblyLoadContext</c> 
        /// and clear references, but full garbage collection depends on the plugin’s <c>Dispose</c> implementation).  
        /// In normal circumstances, unloading an old plugin should not block loading a new version.
        /// </remarks>
        void ForceUnloadPlugin(IPluginContainer pluginContainer);

        /// <summary>
        /// Attempts to unload the specified plugin without affecting other plugins.
        /// </summary>
        /// <param name="pluginContainer">
        /// The plugin container to unload.
        /// </param>
        /// <returns>
        /// <c>true</c> if the plugin was unloaded;  
        /// <c>false</c> if it could not be unloaded because other plugins depend on it.
        /// </returns>
        bool TryUnloadPlugin(IPluginContainer pluginContainer);
    }
    public enum LoadDetails
    {
        /// <summary>The plugin was successfully loaded.</summary>
        Success,
        /// <summary>A plugin with the same <c>FileSignature</c> is already loaded; the existing instance was returned.</summary>
        AlreadyLoaded,
        /// <summary>A different version of the plugin exists at the same location; it must be unloaded before the new version can be loaded.</summary>
        ExistingOldVersion,
        /// <summary>Loading the plugin failed.</summary>
        Failed
    }
    public enum UnloadDetails
    {
        Success,
        DependencyBlocked,
        HostRefused
    }
}
