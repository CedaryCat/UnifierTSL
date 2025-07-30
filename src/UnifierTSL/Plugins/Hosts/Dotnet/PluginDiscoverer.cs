using System.Reflection;
using UnifierTSL.Logging;
using UnifierTSL.Module;
using UnifierTSL.PluginService;

namespace UnifierTSL.Plugins.Hosts.Dotnet
{
    public class PluginDiscoverer : IPluginDiscoverer, ILoggerHost
    {
        private readonly DotnetPluginHost host;
        private readonly RoleLogger Logger;

        public string Name => "UTSL-PluginDisc";
        public string? CurrentLogCategory => null;

        public PluginDiscoverer(DotnetPluginHost host) {
            this.host = host;
            Logger = UnifierApi.CreateLogger(this);
        }

        public IReadOnlyList<IPluginInfo> DiscoverPlugins(string pluginsDirectory, PluginDiscoveryMode discoveryMode) {
            var moduleLoader = new ModuleAssemblyLoader(pluginsDirectory);

            List<ModuleAssemblyInfo> modules = [];

            if (discoveryMode is PluginDiscoveryMode.All) {
                List<Assembly> remove = [];
                foreach (var plugin in host.Plugins) {
                    remove.Add(plugin.PluginAssembly);
                }
                ModuleAssemblyLoader.ClearCache(remove);
                modules.AddRange(moduleLoader.Load(out _));
            }
            else if (discoveryMode is PluginDiscoveryMode.UpdatedOnly) {
                modules.AddRange(moduleLoader.Load(out _));
            }
            else if (discoveryMode is PluginDiscoveryMode.NewOnly) {
                var newers = moduleLoader.Load(out var outdated);
                foreach (var newer in newers) {
                    if (outdated.Any(o => o.Assembly.Location == newer.Assembly.Location)) {
                        continue;
                    }
                    modules.Add(newer);
                }
            }

            List<IPluginInfo> pluginInfos = [];

            foreach (var module in modules) {
                foreach (var type in module.Assembly.DefinedTypes) {
                    if (!type.IsClass
                        || type.IsAbstract
                        || type.IsInterface
                        || !typeof(IPlugin).IsAssignableFrom(type)
                        || !type.GetConstructors().Any(c => !c.IsStatic && c.GetParameters().Length == 0))
                        continue;

                    var metadataAttr = type.GetCustomAttribute<PluginMetadataAttribute>();
                    if (metadataAttr is null) continue;

                    var dependencyAttr = type.GetCustomAttribute<ModuleDependenciesAttribute>();
                    var info = new DotnetPluginInfo(type, module, metadataAttr.ToPluginMetadata());
                    pluginInfos.Add(info);
                }
            }

            return pluginInfos;
        }

        public bool TryDiscoverPlugin(string pluginPath, PluginDiscoveryMode discoveryMode, out IReadOnlyList<IPluginInfo> pluginInfos) {
            var pluginsDirectory = Path.GetDirectoryName(pluginPath)!;
            var moduleLoader = new ModuleAssemblyLoader(pluginsDirectory);

            ModuleAssemblyInfo? module;
            if (discoveryMode is PluginDiscoveryMode.All) {
                var existingAsm = host.Plugins.FirstOrDefault(p => p.PluginAssembly.Location == pluginPath)?.PluginAssembly;
                if (existingAsm is not null) {
                    ModuleAssemblyLoader.ClearCache([existingAsm]);
                }
                if (!moduleLoader.TryLoadSpecific(pluginPath, out module, out _)) {
                    pluginInfos = [];
                    return false;
                }
            }
            else if (discoveryMode is PluginDiscoveryMode.UpdatedOnly) {
                if (!moduleLoader.TryLoadSpecific(pluginPath, out module, out _)) {
                    pluginInfos = [];
                    return false;
                }
                if (module.Signature.QuickEquals(pluginPath)) {
                    pluginInfos = [];
                    return true;
                }
            }
            else if (discoveryMode is PluginDiscoveryMode.NewOnly) {
                if (!moduleLoader.TryLoadSpecific(pluginPath, out module, out var outdated)) {
                    pluginInfos = [];
                    return false;
                }
                if (outdated is not null) {
                    pluginInfos = [];
                    return true;
                }
            }
            else {
                pluginInfos = [];
                return false;
            }

            List<IPluginInfo> infos = [];

            foreach (var type in module.Assembly.DefinedTypes) {
                if (!type.IsClass
                    || type.IsAbstract
                    || type.IsInterface
                    || !typeof(IPlugin).IsAssignableFrom(type)
                    || !type.GetConstructors().Any(c => !c.IsStatic && c.GetParameters().Length == 0))
                    continue;

                var metadataAttr = type.GetCustomAttribute<PluginMetadataAttribute>();
                if (metadataAttr is null) continue;

                var dependencyAttr = type.GetCustomAttribute<ModuleDependenciesAttribute>();
                var info = new DotnetPluginInfo(type, module, metadataAttr.ToPluginMetadata());
                infos.Add(info);
            }

            pluginInfos = infos;
            return true;
        }
    }
}
