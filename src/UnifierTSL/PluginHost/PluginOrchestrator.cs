using System.Collections.Immutable;
using System.Reflection;
using UnifierTSL.Extensions;
using UnifierTSL.Logging;
using UnifierTSL.Module;
using UnifierTSL.PluginHost.Hosts.Dotnet;

namespace UnifierTSL.PluginHost
{
    public class PluginOrchestrator : ILoggerHost
    {
        public static Version ApiVersion => new(1, 0, 0);

        private readonly ImmutableDictionary<string, IPluginHost> registeredPluginHosts;
        private readonly ImmutableArray<IPluginHost> hosts;
        public ImmutableArray<IPluginHost> RegisteredPluginHosts => hosts;

        string ILoggerHost.Name => "PluginOrchestrator";
        string? ILoggerHost.CurrentLogCategory => null;
        private readonly RoleLogger Logger;

        public IPluginHost GetPluginHost(string name) => registeredPluginHosts[name];
        public PluginOrchestrator() {
            Logger = UnifierApi.CreateLogger(this);

            List<IPluginHost> customHosts = ExtractCustomHosts();

            registeredPluginHosts = ImmutableDictionary
                .CreateBuilder<string, IPluginHost>()
                .SetItem("dotnet", new DotnetPluginHost())
                .SetItems(customHosts)
                .ToImmutable();

            hosts = [.. registeredPluginHosts.Values];
        }

        private List<IPluginHost> ExtractCustomHosts() {
            List<IPluginHost> hosts = [];
            ImmutableArray<LoadedModule> modules = new ModuleAssemblyLoader("plugins").Load();
            foreach (LoadedModule module in modules) {
                foreach (TypeInfo type in module.Assembly.DefinedTypes) {
                    if (!type.IsClass
                        || type.IsAbstract
                        || type.IsInterface
                        || !typeof(IPluginHost).IsAssignableFrom(type)
                        || !type.GetConstructors().Any(c => !c.IsStatic && c.GetParameters().Length == 0))
                        continue;

                    PluginHostAttribute? attr = type.GetCustomAttribute<PluginHostAttribute>();
                    if (attr is null) {
                        continue;
                    }

                    if (ApiVersion.Major != attr.ApiVersion.Major // breaking change
                        || ApiVersion.Minor < attr.ApiVersion.Minor // backward compatibility
                        ) {
                        Logger.WarningWithMetadata(
                            category: "Loading",
                            message: GetParticularString("{0} is plugin host class name, {1} is current plugin host version (e.g. 1.0.0)", 
                                $"Plugin host '{type.FullName}' is not compatible with current plugin host version '{ApiVersion}'. Major version must be equal, minor version of plugin host must be less or equal."),
                            metadata: [new("PluginHostFile", module.Assembly.Location)]);
                        continue;
                    }

                    IPluginHost pluginHost;
                    try {
                        pluginHost = (IPluginHost)(Activator.CreateInstance(type) ?? throw new InvalidOperationException($"Activator.CreateInstance returns null when creating '{type.FullName}' instance"));
                    }
                    catch (Exception ex) {
                        Logger.LogHandledExceptionWithMetadata(
                            category: "Loading",
                            message: GetParticularString("{0} is plugin host class name",
                                $"Failed to create instance of plugin host '{type.FullName}'."),
                            metadata: [new("PluginHostFile", module.Assembly.Location)],
                            ex: ex);
                        continue;
                    }
                    hosts.Add(pluginHost);
                }
            }

            return hosts;
        }

        public async Task InitializeAllAsync(CancellationToken cancellationToken = default) {
            foreach (IPluginHost host in hosts) {
                await host.InitializePluginsAsync(cancellationToken);
            }
        }

        public async Task ShutdownAllAsync(CancellationToken cancellationToken = default) {
            foreach (IPluginHost host in hosts) {
                await host.ShutdownAsync(cancellationToken);
            }
        }

        public async Task UnloadAllAsync(CancellationToken cancellationToken = default) {
            foreach (IPluginHost host in hosts) {
                await host.UnloadPluginsAsync(cancellationToken);
            }
        }
    }
}
