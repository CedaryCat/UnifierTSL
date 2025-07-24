using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using UnifierTSL.PluginService.Metadata;

namespace UnifierTSL.PluginService.Loading
{
    internal class PluginLoader(DirectoryInfo directory)
    {
        public PluginDependenicesManager LoadPluginInfo(out IReadOnlyList<PluginTypeInfo> vaildPluginInfos, out IReadOnlyList<PluginTypeInfo> nameConflicts) {
            var pluginDirectory = new DirectoryInfo(Path.Combine(directory.FullName, "ServerPlugins"));
            pluginDirectory.Create();

            Dictionary<string, PluginTypeInfo> loadedPlugins = [];
            List<PluginTypeInfo> conflicts = [];
            foreach (var dll in pluginDirectory.EnumerateFiles("*.dll", SearchOption.AllDirectories)) {
                var asm = AssemblyLoadContext.Default.LoadFromAssemblyPath(dll.FullName);

                foreach (var type in asm.GetTypes()) {

                    if (!type.IsClass
                        || type.IsAbstract
                        || type.IsInterface
                        || !typeof(IPlugin).IsAssignableFrom(type) 
                        || !type.GetConstructors().Any(c => !c.IsStatic && c.GetParameters().Length == 0))
                        continue;

                    var metadataAttr = type.GetCustomAttribute<PluginMetadataAttribute>();
                    if (metadataAttr is null) continue;

                    var dependencyAttr = type.GetCustomAttribute<PluginDependenciesAttribute>();
                    var info = new PluginTypeInfo(type, metadataAttr.ToPluginMetadata(dependencyAttr));

                    if (!loadedPlugins.TryAdd(info.Name, info)) {
                        conflicts.Add(info);
                    }
                }
            }

            foreach (var conflict in conflicts) {
                if (loadedPlugins.Remove(conflict.Name, out var pluginToRemove)) {
                    conflicts.Add(pluginToRemove);
                }
            }

            nameConflicts = [.. conflicts.OrderBy(c => c.Name)];
            vaildPluginInfos = [.. loadedPlugins.Values];

            return new PluginDependenicesManager(directory, vaildPluginInfos);
        }
    }
}
