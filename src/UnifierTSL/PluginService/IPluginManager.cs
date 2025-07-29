using System.Diagnostics.CodeAnalysis;
using UnifierTSL.Module;
using UnifierTSL.PluginService.Metadata;

namespace UnifierTSL.PluginService;

/// <summary>
/// Represents a manager for plugins, providing methods to load, retrieve, and check plugins.
/// </summary>
public interface IPluginManager : IModule, IDisposable
{
    /// <summary>
    /// Gets the type of the plugin manager.
    /// </summary>
    public string Type { get; }

    /// <summary>
    /// Determines whether a plugin with the specified name exists.
    /// </summary>
    /// <param name="pluginName">The name of the plugin.</param>
    /// <returns>True if the plugin exists; otherwise, false.</returns>
    public bool Contains(string pluginName);

    /// <summary>
    /// Determines whether a plugin with the specified metadata exists.
    /// </summary>
    /// <param name="metadata">The metadata of the plugin.</param>
    /// <returns>True if the plugin exists; otherwise, false.</returns>
    public bool Contains(PluginMetadata metadata);

    /// <summary>
    /// Gets the plugin with the specified name.
    /// </summary>
    /// <param name="pluginName">The name of the plugin.</param>
    /// <returns>The plugin module.</returns>
    public IModule GetPlugin(string pluginName);

    /// <summary>
    /// Attempts to get the plugin with the specified name.
    /// </summary>
    /// <param name="pluginName">The name of the plugin.</param>
    /// <param name="plugin">The plugin module if found; otherwise, null.</param>
    /// <returns>True if the plugin was found; otherwise, false.</returns>
    public bool TryGetPlugin(string pluginName, [NotNullWhen(true)] out IModule? plugin);

    /// <summary>
    /// Gets the plugin with the specified metadata.
    /// </summary>
    /// <param name="metadata">The metadata of the plugin.</param>
    /// <returns>The plugin module.</returns>
    public IModule GetPlugin(PluginMetadata metadata);

    /// <summary>
    /// Attempts to get the plugin with the specified metadata.
    /// </summary>
    /// <param name="metadata">The metadata of the plugin.</param>
    /// <param name="plugin">The plugin module if found; otherwise, null.</param>
    /// <returns>True if the plugin was found; otherwise, false.</returns>
    public bool TryGetPlugin(PluginMetadata metadata, [NotNullWhen(true)] out IModule? plugin);

    /// <summary>
    /// Gets all loaded plugins.
    /// </summary>
    /// <returns>A read-only list of all plugin modules.</returns>
    public IReadOnlyList<IModule> GetAllPlugins();
}
