using System.Collections.Immutable;
using UnifierTSL.Module;

namespace UnifierTSL.PluginHost
{
    public class PluginOrchestrator
    {
        readonly Dictionary<string, IPluginHost> RegisteredPluginHosts = [];
        public PluginOrchestrator() { 

        }
    }
}
