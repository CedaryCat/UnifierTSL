using UnifierTSL.Plugins;
using UnifierTSL.PluginServices;

namespace UnifierTSL.PluginService
{
    public interface IPluginContainer : IPluginMetadata
    {
        PluginMetadata Metadata { get; }
        string IPluginMetadata.Name => Metadata.Name;
        string IPluginMetadata.Author => Metadata.Author;
        string IPluginMetadata.Description => Metadata.Description;
        Version IPluginMetadata.Version => Metadata.Version;
        IPlugin Plugin { get; }
        PluginLoadStatus LoadStatus { get; }
        public Exception? LoadError { get; }
        PluginStatus Status { get; set; }
    }
    public enum PluginLoadStatus
    {
        NotLoaded,
        Loaded,
        Failed,
        Unloaded
    }
    public enum PluginStatus {
        Enabled,
        Disabled
    }
}
