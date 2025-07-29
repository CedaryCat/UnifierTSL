using UnifierTSL.PluginService.Metadata;

namespace UnifierTSL.Module
{
    public interface IModule
    {
        public string Name { get; }
        public PluginMetadata Metadata { get; }
    }
}
