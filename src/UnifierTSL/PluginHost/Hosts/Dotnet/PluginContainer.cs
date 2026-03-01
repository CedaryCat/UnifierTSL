using System.Reflection;
using UnifierTSL.FileSystem;
using UnifierTSL.Module;
using UnifierTSL.Plugins;
using UnifierTSL.PluginService;

namespace UnifierTSL.PluginHost.Hosts.Dotnet
{
    public class PluginContainer(PluginMetadata metadata, LoadedModule module, IPlugin plugin) : IPluginContainer
    {
        public PluginMetadata Metadata => metadata;

        public string Name => metadata.Name;
        public string Author => metadata.Author;
        public string Description => metadata.Description;
        public Version Version => metadata.Version;
        public IPlugin Plugin => plugin;
        public FileSignature Location => module.Signature;
        public LoadedModule Module => module;
        public Assembly PluginAssembly => module.Assembly;

        public PluginLoadStatus LoadStatus { get; internal set; }
        public Exception? LoadError { get; internal set; }
        public PluginStatus Status { get; set; }
    }
}
