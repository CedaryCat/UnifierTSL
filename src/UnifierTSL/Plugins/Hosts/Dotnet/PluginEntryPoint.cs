using UnifierTSL.Plugins;

namespace UnifierTSL.Plugins.Hosts.Dotnet
{
    public class PluginEntryPoint(Type pluginType) : IPluginEntryPoint
    {
        public object EntryPoint => pluginType;
        public string EntryPointString => pluginType.FullName!;
    }
}
