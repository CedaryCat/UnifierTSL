using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UnifierTSL.PluginHost
{
    public interface IPluginEntryPoint
    {
        object EntryPoint { get; }
        string EntryPointString { get; }
    }
}
