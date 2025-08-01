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

        readonly ImmutableDictionary<string, IPluginHost> registeredPluginHosts;
        readonly ImmutableArray<IPluginHost> hosts;
        public ImmutableArray<IPluginHost> RegisteredPluginHosts => hosts;

        string ILoggerHost.Name => "PluginOrchestrator";
        string? ILoggerHost.CurrentLogCategory => null;
        readonly RoleLogger Logger;

        public IPluginHost GetPluginHost(string name) => registeredPluginHosts[name];
        public PluginOrchestrator() {
            Logger = UnifierApi.CreateLogger(this);

            var customHosts = ExtractCustomHosts();

            registeredPluginHosts = ImmutableDictionary
                .CreateBuilder<string, IPluginHost>()
                .SetItem("dotnet", new DotnetPluginHost())
                .SetItems(customHosts)
                .ToImmutable();

            hosts = [.. registeredPluginHosts.Values];
        }

        private List<IPluginHost> ExtractCustomHosts() {
            List<IPluginHost> hosts = [];
            var modules = new ModuleAssemblyLoader("plugins").Load(out _);
            foreach (var module in modules) {
                foreach (var type in module.Assembly.DefinedTypes) {
                    if (!type.IsClass
                        || type.IsAbstract
                        || type.IsInterface
                        || !typeof(IPluginHost).IsAssignableFrom(type)
                        || !type.GetConstructors().Any(c => !c.IsStatic && c.GetParameters().Length == 0))
                        continue;

                    var attr = type.GetCustomAttribute<PluginHostAttribute>();
                    if (attr is null) {
                        continue;
                    }

                    if (ApiVersion.Major != attr.ApiVersion.Major // breaking change
                        || ApiVersion.Minor < attr.ApiVersion.Minor // backward compatibility
                        ) {
                        Logger.WarningWithMetadata(
                            category: "Loading",
                            message: $"Plugin host {type.FullName} is not compatible with current plugin host version {ApiVersion}\r\n" +
                            $"Major version must be equal, Minor version of plugin host must be less or equal.",
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
                            message: $"Failed to create instance of plugin host {type.FullName}.",
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
            foreach (var host in hosts) { 
                await host.InitializePluginsAsync(cancellationToken);
            }
        }

        public async Task ShutdownAllAsync(CancellationToken cancellationToken = default) {
            foreach (var host in hosts) {
                await host.ShutdownAsync(cancellationToken);
            }
        }

        public async Task UnloadAllAsync(CancellationToken cancellationToken = default) {
            foreach (var host in hosts) {
                await host.UnloadPluginsAsync(cancellationToken);
            }
        }
    }
}
