using UnifierTSL.PluginServices;

namespace UnifierTSL.PluginService
{
    public interface IPluginContainer {
        PluginMetadata Metadata { get; }
        string Name => Metadata.Name;
        string Author => Metadata.Author;
        string Description => Metadata.Description;
        Version Version => Metadata.Version;
        IPlugin Plugin { get; }
    }
}
