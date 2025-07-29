using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnifierTSL.PluginService.Metadata;

namespace UnifierTSL.Module
{
    public interface IModule
    {
        public string Name { get; }

        public PluginMetadata Metadata { get; }
    }
}
