using System.Collections.Immutable;
using UnifierTSL.Logging;
using UnifierTSL.Module;
using UnifierTSL.Plugins;
using UnifierTSL.PluginService;

namespace UnifierTSL.PluginHost.Hosts.Dotnet
{
    public class PluginLoader : IPluginLoader, ILoggerHost
    {
        private readonly DotnetPluginHost host;
        private readonly RoleLogger Logger;
        public PluginLoader(DotnetPluginHost host) {
            this.host = host;
            this.Logger = host.Logger;
        }

        public string Name => "UTSL-PluginLoader";
        public string? CurrentLogCategory => null;

        public void ForceUnloadPlugin(IPluginContainer pluginContainer) {
            if (pluginContainer is not PluginContainer container) {
                Logger.Warning(
                    category: "Unloading",
                    message: $"Plugins {pluginContainer.Name} is not a .Net Plugin, skipping.");
                return;
            }
            var loader = new ModuleAssemblyLoader("plugins");
            loader.ForceUnload(container.Module);
        }

        public bool TryUnloadPlugin(IPluginContainer pluginContainer) {
            if (pluginContainer is not PluginContainer container) {
                Logger.Warning(
                    category: "Unloading",
                    message: $"Plugins {pluginContainer.Name} is not a .Net Plugin, skipping.");
                return false;
            }

            var loader = new ModuleAssemblyLoader("plugins");
            if (container.Module.CoreModule is not null) {
                return false;
            }

            if (container.Module.DependentModules.Length > 0) {
                return false;
            }

            loader.ForceUnload(container.Module);
            return true;
        }

        public IPluginContainer? LoadPlugin(IPluginInfo pluginInfo, out LoadDetails loadDetails) {
            if (pluginInfo is not DotnetPluginInfo info) { 
                Logger.Warning(
                    category: "Loading",
                    message: $"Plugins {pluginInfo.Name} is not a DotnetPluginInfo, skipping.");
                loadDetails = default;
                return null;
            }

            var loader = new ModuleAssemblyLoader("plugins");
            if (!loader.TryLoadSpecific(info.Module, out var loaded, out var details)) {
                switch (details) {
                    case ModuleLoadResult.InvalidLibrary:
                    case ModuleLoadResult.CoreModuleNotFound:
                    case ModuleLoadResult.Failed:
                        loadDetails = LoadDetails.Failed;
                        return null;
                    case ModuleLoadResult.ExistingOldVersion:
                        loadDetails = LoadDetails.ExistingOldVersion;
                        return null;
                    default:
                        throw new Exception();
                }
            }
            var type = loaded.Assembly.GetType(info.EntryPoint.EntryPointString);
            if (type is null) {
                loadDetails = LoadDetails.Failed;
                return null;
            }

            IPlugin instance;
            try {
                var boxed = Activator.CreateInstance(type) ?? throw new InvalidOperationException("Failed to create instance");
                if (boxed is not IPlugin) {
                    Logger.WarningWithMetadata(
                        category: "Loading",
                        message: $"Plugin {info.Name} is not an IPlugin, skipping.",
                    metadata: [new("PluginFile", info.Location.FilePath)]);
                    loadDetails = LoadDetails.Failed;
                    return null;
                }
                instance = (IPlugin)boxed;
            }
            catch (Exception ex) {
                Logger.LogHandledExceptionWithMetadata(
                    category: "Loading",
                    message: $"Failed to create instance of plugin {info.Name}, type: {type.FullName}",
                    metadata: [new("PluginFile", info.Location.FilePath)],
                    ex: ex);
                loadDetails = LoadDetails.Failed;
                return null;
            }

            loaded.Context.AddDisposeAction(async () => await instance.DisposeAsync());

            var container = new PluginContainer(info.Metadata, loaded, instance);
            ImmutableInterlocked.Update(ref host.Plugins, p => p.Add(container));
            loadDetails = LoadDetails.Success;
            return container;
        }
    }
}
