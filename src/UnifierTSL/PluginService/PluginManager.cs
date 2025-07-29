using NuGet.Protocol.Plugins;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using UnifierTSL.Module;
using UnifierTSL.PluginService.Metadata;

namespace UnifierTSL.PluginService;

public abstract class PluginManager<T> : IPluginManager
    where T : class, IModule
{
    private bool disposedValue;

    private readonly Lock _lock = new();

    private readonly HashSet<T> modules = [];

    public abstract string Type { get; }

    string IModule.Name => throw new NotImplementedException();

    PluginMetadata IModule.Metadata => throw new NotImplementedException();

    IModule IPluginManager.GetPlugin(string pluginName) => GetPlugin(pluginName);
    IModule IPluginManager.GetPlugin(PluginMetadata metadata) => GetPlugin(metadata);

    bool IPluginManager.TryGetPlugin(string pluginName, [NotNullWhen(true)] out IModule? plugin)
    {
        if (TryGetPlugin(pluginName, out T? typedPlugin))
        {
            plugin = typedPlugin;
            return true;
        }
        plugin = null;
        return false;
    }

    bool IPluginManager.TryGetPlugin(PluginMetadata metadata, [NotNullWhen(true)] out IModule? plugin)
    {
        if (TryGetPlugin(metadata, out T? typedPlugin))
        {
            plugin = typedPlugin;
            return true;
        }
        plugin = null;
        return false;
    }

    public virtual bool Contains(string pluginName) => (from module in modules
                                                        where module.Name == pluginName
                                                        select module).Any();

    public virtual bool Contains(PluginMetadata metadata) => (from module in modules
                                                              where module.Metadata == metadata
                                                              select module).Any();

    public virtual IReadOnlyList<IModule> GetAllPlugins() => modules.ToList().AsReadOnly();

    public virtual bool TryGetPlugin(string pluginName, [NotNullWhen(true)] out T? plugin)
    {
        plugin = null;
        if (string.IsNullOrEmpty(pluginName))
            return false;
        lock (_lock)
        {
            plugin = modules.FirstOrDefault(m => m.Name == pluginName);
        }
        return plugin != null;
    }

    public virtual bool TryGetPlugin(PluginMetadata metadata, [NotNullWhen(true)] out T? plugin)
    {
        plugin = null;
        lock (_lock)
        {
            plugin = modules.FirstOrDefault(m => m.Metadata == metadata);
        }
        return plugin != null;
    }

    public virtual T GetPlugin(string pluginName)
    {
        _ = TryGetPlugin(pluginName, out var plugin);
        if (plugin is null)
            throw new KeyNotFoundException($"Plugin with name '{pluginName}' not found.");
        if (plugin is not T typedPlugin)
            throw new InvalidCastException($"Plugin with name '{pluginName}' is not of type '{typeof(T).FullName}'.");
        return typedPlugin;
    }

    public virtual T GetPlugin(PluginMetadata metadata)
    {
        _ = TryGetPlugin(metadata, out var plugin);
        if (plugin is null)
            throw new KeyNotFoundException($"Plugin with name '{metadata}' not found.");
        if (plugin is not T typedPlugin)
            throw new InvalidCastException($"Plugin with name '{metadata}' is not of type '{typeof(T).FullName}'.");
        return typedPlugin;
    }

    protected void AddPlugin(T plugin)
    {
        lock (_lock)
        {
            modules.Add(plugin);
        }
    }

    protected void RemovePlugin(T plugin)
    {
        lock (_lock)
        {
            modules.Remove(plugin);
        }
    }

    protected void RemovePlugin(PluginMetadata metadata)
    {
        lock (_lock)
        {
            modules.Remove(GetPlugin(metadata));
        }
    }

    protected void RemovePlugin(string pluginName)
    {
        lock (_lock)
        {
            modules.Remove(GetPlugin(pluginName));
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            disposedValue = true;
        }
    }

    void IDisposable.Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
