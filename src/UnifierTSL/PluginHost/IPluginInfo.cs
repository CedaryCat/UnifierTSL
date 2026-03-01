using UnifierTSL.FileSystem;
using UnifierTSL.Plugins;

namespace UnifierTSL.PluginHost
{
    public interface IPluginInfo : IPluginMetadata
    {
        PluginMetadata Metadata { get; }
        string IPluginMetadata.Name => Metadata.Name;
        Version IPluginMetadata.Version => Metadata.Version;
        string IPluginMetadata.Author => Metadata.Author;
        string IPluginMetadata.Description => Metadata.Description;
        FileSignature Location { get; }
        IPluginEntryPoint EntryPoint { get; }
    }
}
