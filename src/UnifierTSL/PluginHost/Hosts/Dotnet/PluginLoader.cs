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
                    message: GetParticularString("{0} is plugin name", 
                        $"Plugin '{pluginContainer.Name}' is not a .NET Plugin, skipping."));
                return;
            }
            ModuleAssemblyLoader loader = new("plugins");
            loader.ForceUnload(container.Module);
        }

        public bool TryUnloadPlugin(IPluginContainer pluginContainer) {
            if (pluginContainer is not PluginContainer container) {
                Logger.Warning(
                    category: "Unloading",
                    message: GetParticularString("{0} is plugin name",
                        $"Plugin '{pluginContainer.Name}' is not a .NET Plugin, skipping."));
                return false;
            }

            ModuleAssemblyLoader loader = new("plugins");
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
                    message: GetParticularString("{0} is plugin name",
                        $"Plugin '{pluginInfo.Name}' is not a DotnetPluginInfo, skipping."));
                loadDetails = default;
                return null;
            }

            ModuleAssemblyLoader loader = new("plugins");
            if (!loader.TryLoadSpecific(info.Module, out LoadedModule? loaded, out ModuleLoadResult details)) {
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
            Type? type = loaded.Assembly.GetType(info.EntryPoint.EntryPointString);
            if (type is null) {
                loadDetails = LoadDetails.Failed;
                return null;
            }

            IPlugin instance;
            try {
                object boxed = Activator.CreateInstance(type) ?? throw new InvalidOperationException("Failed to create instance");
                if (boxed is not IPlugin) {
                    Logger.WarningWithMetadata(
                        category: "Loading",
                        message: GetParticularString("{0} is plugin name (or type name)", 
                            $"Type '{info.Name}' does not implement IPlugin. Skipped."),
                    metadata: [new("PluginFile", info.Location.FilePath)]);
                    loadDetails = LoadDetails.Failed;
                    return null;
                }
                instance = (IPlugin)boxed;
            }
            catch (Exception ex) {
                Logger.LogHandledExceptionWithMetadata(
                    category: "Loading",
                    message: GetParticularString("{0} is plugin name, {1} is type full name", 
                        $"Failed to create instance of plugin '{info.Name}', type: '{type.FullName}'."),
                    metadata: [new("PluginFile", info.Location.FilePath)],
                    ex: ex);
                loadDetails = LoadDetails.Failed;
                return null;
            }

            loaded.Context.AddDisposeAction(async () => await instance.DisposeAsync());

            PluginContainer container = new(info.Metadata, loaded, instance);
            ImmutableInterlocked.Update(ref host.Plugins, p => p.Add(container));
            loadDetails = LoadDetails.Success;
            return container;
        }
    }
}
