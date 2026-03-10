using System.Collections.Immutable;
using System.Reflection;
using UnifierTSL.Extensions;
using UnifierTSL.FileSystem;
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

        internal PluginContainer? LoadPluginCandidate(DotnetPluginInfo info, out LoadDetails loadDetails) {
            FileInfo file = new(info.Location.FilePath);
            if (!file.Exists) {
                Logger.WarningWithMetadata(
                    category: "HotReload",
                    message: GetParticularString("{0} is plugin file path", $"Candidate plugin file '{info.Location.FilePath}' does not exist."),
                    metadata: [new("PluginFile", info.Location.FilePath)]);
                loadDetails = LoadDetails.Failed;
                return null;
            }

            ModuleLoadContext context = new(file);
            Assembly? loadedAssembly = null;
            try {
                loadedAssembly = context.LoadFromStream(info.Location.FilePath);
                Type? type = loadedAssembly.GetType(info.EntryPoint.EntryPointString);
                if (type is null) {
                    Logger.WarningWithMetadata(
                        category: "HotReload",
                        message: GetParticularString("{0} is plugin entry point", $"Candidate entry point '{info.EntryPoint.EntryPointString}' was not found."),
                        metadata: [new("PluginFile", info.Location.FilePath)]);
                    context.Unload();
                    loadDetails = LoadDetails.Failed;
                    return null;
                }

                if (!TryCreatePluginInstance(info, type, out IPlugin instance)) {
                    context.Unload();
                    loadDetails = LoadDetails.Failed;
                    return null;
                }

                context.AddDisposeAction(async () => await instance.DisposeAsync());
                LoadedModule module = new(context, loadedAssembly, [], FileSignature.Generate(info.Location.FilePath), null);
                PluginContainer container = new(info.Metadata, module, instance);
                loadDetails = LoadDetails.Success;
                return container;
            }
            catch (Exception ex) {
                Logger.LogHandledExceptionWithMetadata(
                    category: "HotReload",
                    message: GetParticularString("{0} is plugin file path", $"Failed to load candidate plugin from '{info.Location.FilePath}'."),
                    metadata: [new("PluginFile", info.Location.FilePath)],
                    ex: ex);
                try {
                    context.Unload();
                }
                catch { }
                loadDetails = LoadDetails.Failed;
                return null;
            }
        }

        internal void UnloadCandidate(PluginContainer candidate) {
            try {
                candidate.Module.Unload();
            }
            finally {
                candidate.LoadStatus = PluginLoadStatus.Unloaded;
            }
        }

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
            RemoveUnloadedContainers();
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
            RemoveUnloadedContainers();
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

            if (!TryCreatePluginInstance(info, type, out IPlugin instance)) {
                loadDetails = LoadDetails.Failed;
                return null;
            }

            loaded.Context.AddDisposeAction(async () => await instance.DisposeAsync());

            PluginContainer container = new(info.Metadata, loaded, instance);
            ImmutableInterlocked.Update(ref host.Plugins, p => p.Add(container));
            loadDetails = LoadDetails.Success;
            return container;
        }

        private void RemoveUnloadedContainers() {
            ImmutableInterlocked.Update(ref host.Plugins, static plugins => {
                bool changed = false;
                ImmutableArray<PluginContainer>.Builder kept = ImmutableArray.CreateBuilder<PluginContainer>(plugins.Length);

                foreach (PluginContainer plugin in plugins) {
                    if (plugin.Module.Unloaded) {
                        plugin.LoadStatus = PluginLoadStatus.Unloaded;
                        changed = true;
                        continue;
                    }
                    kept.Add(plugin);
                }

                return changed
                    ? [.. kept]
                    : plugins;
            });
        }

        private bool TryCreatePluginInstance(DotnetPluginInfo info, Type type, out IPlugin instance) {
            instance = null!;
            try {
                object boxed = Activator.CreateInstance(type) ?? throw new InvalidOperationException("Failed to create instance");
                if (boxed is not IPlugin plugin) {
                    Logger.WarningWithMetadata(
                        category: "Loading",
                        message: GetParticularString("{0} is plugin name (or type name)",
                            $"Type '{info.Name}' does not implement IPlugin. Skipped."),
                        metadata: [new("PluginFile", info.Location.FilePath)]);
                    return false;
                }

                instance = plugin;
                return true;
            }
            catch (Exception ex) {
                Logger.LogHandledExceptionWithMetadata(
                    category: "Loading",
                    message: GetParticularString("{0} is plugin name, {1} is type full name",
                        $"Failed to create instance of plugin '{info.Name}', type: '{type.FullName}'."),
                    metadata: [new("PluginFile", info.Location.FilePath)],
                    ex: ex);
                return false;
            }
        }
    }
}
