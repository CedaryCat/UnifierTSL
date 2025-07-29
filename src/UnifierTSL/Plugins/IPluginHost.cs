using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnifierTSL.PluginService;

namespace UnifierTSL.Plugins
{
    public interface IPluginHost { 
        public string Name { get; }
        IReadOnlyList<IPlugin> Plugins { get; }
    }
    public interface IPluginHost<TPlugin> : IPluginHost where TPlugin : IPlugin
    {
        new IReadOnlyList<TPlugin> Plugins { get; }
        IReadOnlyList<IPlugin> IPluginHost.Plugins => Plugins;
    }
}
