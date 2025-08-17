using UnifierTSL.PluginHost;

namespace UnifierTSL.PluginHost.Hosts.Dotnet
{
    public class PluginEntryPoint(string pluginTypeFullName) : IPluginEntryPoint
    {
        public object EntryPoint => pluginTypeFullName;
        public string EntryPointString => pluginTypeFullName;
    }
}
