using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnifierTSL.Module.Dependencies;
using UnifierTSL.PluginService.Metadata;

namespace UnifierTSL.PluginService
{
    public class PluginContainer(PluginMetadata metadata, IReadOnlyList<ModuleDependency> dependencies, IPlugin plugin)
    {
        private readonly ImmutableArray<ModuleDependency> dependencies = [.. dependencies];
        private readonly Assembly pluginAssembly = plugin.GetType().Assembly;

        public string Name => metadata.Name;
        public string Author => metadata.Author;
        public string Description => metadata.Description;
        public Version Version => metadata.Version;
        public IPlugin Plugin => plugin;
        public ImmutableArray<ModuleDependency> Dependencies => dependencies;
        public Assembly PluginAssembly => pluginAssembly;
    }
}
