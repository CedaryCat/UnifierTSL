using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnifierTSL.Logging;
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

        public IPluginContainer? LoadPlugin(IPluginInfo pluginInfo, bool addToHost = true) {
            if (pluginInfo is not DotnetPluginInfo info) {
                Logger.Warning(
                    category: "Loading",
                    message: $"Plugins {pluginInfo.Name} is not a DotnetPluginInfo, skipping.");
                return null;
            }

            IPlugin instance;
            try {
                instance = (IPlugin)(Activator.CreateInstance(info.PluginType) ?? throw new InvalidOperationException("Failed to create instance"));
            }
            catch (Exception ex) {
                Logger.LogHandledExceptionWithMetadata(
                    category: "Loading",
                    message: $"Failed to create instance of plugin {info.Name}, type: {info.PluginType.FullName}",
                    metadata: [new("PluginFile", info.Location.FilePath)],
                    ex: ex);
                return null;
            }

            var container = new PluginContainer(info.Metadata, info.Module, instance);
            if (addToHost) {
                ImmutableInterlocked.Update(ref host.Plugins, p => p.Add(container));
            }

            return container;
        }
    }
}
