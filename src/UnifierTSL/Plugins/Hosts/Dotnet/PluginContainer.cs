using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnifierTSL.Module.Dependencies;
using UnifierTSL.PluginService;
using UnifierTSL.PluginServices;

namespace UnifierTSL.Plugins.Hosts.Dotnet
{
    public class PluginContainer(PluginMetadata metadata, ImmutableArray<ModuleDependency> dependencies, IPlugin plugin) : IPluginContainer
    {
        private readonly ImmutableArray<ModuleDependency> dependencies = [.. dependencies];
        private readonly Assembly pluginAssembly = plugin.GetType().Assembly;
        public PluginMetadata Metadata => metadata;

        public string Name => metadata.Name;
        public string Author => metadata.Author;
        public string Description => metadata.Description;
        public Version Version => metadata.Version;
        public IPlugin Plugin => plugin;
        public ImmutableArray<ModuleDependency> Dependencies => dependencies;
        public Assembly PluginAssembly => pluginAssembly;
    }
}
