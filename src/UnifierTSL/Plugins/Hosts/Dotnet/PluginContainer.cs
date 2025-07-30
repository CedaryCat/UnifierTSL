using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnifierTSL.Module;
using UnifierTSL.Module.Dependencies;
using UnifierTSL.PluginService;
using UnifierTSL.PluginServices;

namespace UnifierTSL.Plugins.Hosts.Dotnet
{
    public class PluginContainer(PluginMetadata metadata, ModuleAssemblyInfo module, IPlugin plugin) : IPluginContainer
    {
        public PluginMetadata Metadata => metadata;

        public string Name => metadata.Name;
        public string Author => metadata.Author;
        public string Description => metadata.Description;
        public Version Version => metadata.Version;
        public IPlugin Plugin => plugin;
        public ModuleAssemblyInfo Module => module;
        public Assembly PluginAssembly => module.Assembly;

        public PluginLoadStatus LoadStatus { get; internal set; }
        public Exception? LoadError { get; internal set; }
        public PluginStatus Status { get; set; }
    }
}
