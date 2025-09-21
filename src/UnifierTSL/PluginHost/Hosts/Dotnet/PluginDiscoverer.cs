using System.Reflection;
using System.Reflection.Metadata;
using UnifierTSL.Logging;
using UnifierTSL.Module;
using UnifierTSL.Plugins;
using UnifierTSL.Reflection.Metadata;

namespace UnifierTSL.PluginHost.Hosts.Dotnet
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
            ModuleAssemblyLoader moduleLoader = new(pluginsDirectory);
            List<ModulePreloadInfo> modules = moduleLoader.PreloadModules(ModuleSearchMode.Any).ToList();

            foreach (PluginContainer plugin in host.Plugins) {
                if (discoveryMode is PluginDiscoveryMode.All) {
                    continue;
                }
                else if (discoveryMode is PluginDiscoveryMode.UpdatedOnly) {
                    ModulePreloadInfo? info = modules.FirstOrDefault(m => m.FileSignature.FilePath == plugin.Location.FilePath);
                    if (info is null) {
                        continue;
                    }
                    if (info.FileSignature.Hash == plugin.Location.Hash) {
                        modules.Remove(info);
                    }
                    // moduleLoader.ForceUnload(plugin.Module);
                }
                else if (discoveryMode is PluginDiscoveryMode.NewOnly) {
                    ModulePreloadInfo? info = modules.FirstOrDefault(m => m.FileSignature.FilePath == plugin.Location.FilePath);
                    if (info is null) {
                        continue;
                    }
                    modules.Remove(info);
                }
            }

            List<IPluginInfo> pluginInfos = [];
            foreach (ModulePreloadInfo? module in modules) {
                pluginInfos.AddRange(ExtractPluginInfos(module));
            }

            return pluginInfos;
        }

        private static List<DotnetPluginInfo> ExtractPluginInfos(ModulePreloadInfo module) {
            using FileStream stream = File.OpenRead(module.FileSignature.FilePath);
            MetadataReader? reader = MetadataBlobHelpers.GetPEReader(stream)?.GetMetadataReader();
            if (reader is null)
                return [];
            List<DotnetPluginInfo> pluginInfos = [];
            foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions) {
                TypeDefinition typeDef = reader.GetTypeDefinition(typeHandle);
                if ((typeDef.Attributes & TypeAttributes.ClassSemanticsMask) == TypeAttributes.Interface)
                    continue;
                if (typeDef.Attributes.HasFlag(TypeAttributes.Abstract))
                    continue;
                if (!MetadataBlobHelpers.HasDefaultConstructor(reader, typeDef))
                    continue;
                if (!MetadataBlobHelpers.TryReadTypeAttributeData(reader, typeDef, typeof(PluginMetadataAttribute).FullName!, out ParsedCustomAttribute metadataAttr))
                    continue;
                PluginMetadata metadata = PluginMetadataAttribute.FromAttributeMetadata(metadataAttr);
                pluginInfos.Add(new DotnetPluginInfo(reader.GetString(typeDef.Namespace), reader.GetString(typeDef.Name), module, metadata));
            }
            return pluginInfos;
        }

        public bool TryDiscoverPlugin(string pluginPath, PluginDiscoveryMode discoveryMode, out IReadOnlyList<IPluginInfo> pluginInfos) {
            string pluginsDirectory = Path.GetDirectoryName(pluginPath)!;
            ModuleAssemblyLoader moduleLoader = new(pluginsDirectory);
            ModulePreloadInfo? info = moduleLoader.PreloadModule(pluginPath);
            if (info is null) {
                pluginInfos = [];
                return false;
            }

            bool canDiscover = false;
            if (discoveryMode is PluginDiscoveryMode.All) {
                canDiscover = true;
            }
            else if (discoveryMode is PluginDiscoveryMode.UpdatedOnly) {
                if (!moduleLoader.TryGetExistingModule(info.FileSignature.FilePath, out LoadedModule? existingModule) || existingModule.Signature.Hash != info.FileSignature.Hash) {
                    canDiscover = true;
                }
            }
            else if (discoveryMode is PluginDiscoveryMode.NewOnly) {
                if (!moduleLoader.TryGetExistingModule(info.FileSignature.FilePath, out LoadedModule? existingModule)) {
                    canDiscover = true;
                }
            }

            if (canDiscover) {
                pluginInfos = ExtractPluginInfos(info);
                return true;
            }

            pluginInfos = [];
            return true;
        }
    }
}
