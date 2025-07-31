using UnifierTSL.Module;
using UnifierTSL.Plugins;

namespace UnifierTSL.PluginHost.Hosts.Dotnet
{
    public record DotnetPluginInfo(Type PluginType, ModuleAssemblyInfo Module, PluginMetadata Metadata) : IPluginInfo
    {
        public string Name => PluginType.Name;
        public IPluginEntryPoint EntryPoint { get; init; } = new PluginEntryPoint(PluginType);
        public FileSignature Location => Module.Signature;
    }
}
