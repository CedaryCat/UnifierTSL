using UnifierTSL.FileSystem;
using UnifierTSL.Module;
using UnifierTSL.Plugins;

namespace UnifierTSL.PluginHost.Hosts.Dotnet
{
    public record DotnetPluginInfo(string PluginTypeNameSpace, string PluginTypeName, ModulePreloadInfo Module, PluginMetadata Metadata) : IPluginInfo
    {
        public string Name => PluginTypeName;
        public IPluginEntryPoint EntryPoint { get; init; } = new PluginEntryPoint($"{PluginTypeNameSpace}.{PluginTypeName}");
        public FileSignature Location => Module.FileSignature;
    }
}
