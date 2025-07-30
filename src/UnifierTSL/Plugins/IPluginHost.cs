using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnifierTSL.Logging;
using UnifierTSL.PluginService;

namespace UnifierTSL.Plugins
{
    public interface IPluginHost : ILoggerHost { 
        public string Key { get; }
        IReadOnlyList<IPluginContainer> Plugins { get; }
        IPluginDiscoverer PluginDiscoverer { get; }
        IPluginLoader PluginLoader { get; }

        Task InitializePluginsAsync(CancellationToken cancellationToken = default);
        Task UnloadPluginsAsync(CancellationToken cancellationToken = default);
        Task ShutdownAsync(CancellationToken cancellationToken = default);
    }
}
