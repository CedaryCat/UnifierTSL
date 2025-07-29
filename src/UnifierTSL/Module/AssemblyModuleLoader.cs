using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnifierTSL.Logging;

namespace UnifierTSL.Module
{
    public abstract class AssemblyModuleLoader<TModule>(string loadDirectory) : ILoggerHost where TModule : IModule
    {
        public abstract string Name { get; }
        public abstract string CurrentLogCategory { get; set; }

        public void PreloadModules()
        {
            var searchDir = new DirectoryInfo(loadDirectory);
            foreach (var file in searchDir.GetFiles("*.dll"))
            {

            }
        }
    }
}
