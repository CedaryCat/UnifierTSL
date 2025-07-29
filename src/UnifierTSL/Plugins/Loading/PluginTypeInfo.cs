using UnifierTSL.PluginServices;

namespace UnifierTSL.PluginService.Loading
{
    public record PluginTypeInfo(Type PluginType, PluginMetadata Metadata) {
        public string Name => PluginType.Name;
    }
}
