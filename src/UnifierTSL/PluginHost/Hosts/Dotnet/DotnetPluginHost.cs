using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnifierTSL.Logging;
using UnifierTSL.PluginService;

namespace UnifierTSL.PluginHost.Hosts.Dotnet
{
    public partial class DotnetPluginHost : IPluginHost
    {
        public ImmutableArray<PluginContainer> Plugins = [];
        public RoleLogger Logger { get; init; }

        public string Name => "UTSL-PluginHost";
        public string Key => "dotnet";
        public string? CurrentLogCategory => null;

        IReadOnlyList<IPluginContainer> IPluginHost.Plugins => Plugins;
        public IPluginDiscoverer PluginDiscoverer { get; init; }
        public IPluginLoader PluginLoader { get; init; }
        public DotnetPluginHost() {
            Logger = UnifierApi.CreateLogger(this);
            PluginDiscoverer = new PluginDiscoverer(this);
            PluginLoader = new PluginLoader(this);
        }

        public Task ShutdownAsync(CancellationToken cancellationToken = default) {
#warning TODO
            return Task.CompletedTask;
        }

        public Task UnloadPluginsAsync(CancellationToken cancellationToken = default) {
#warning TODO
            return Task.CompletedTask;
        }
    }
}
