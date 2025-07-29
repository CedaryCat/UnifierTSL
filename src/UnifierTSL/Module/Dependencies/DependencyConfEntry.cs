using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UnifierTSL.Module.Dependencies
{
    public class DependencyConfEntry
    {
        public required bool IsNativeLibrary { get; set; }
        public required string Name { get; set; }
        public required Version Version { get; set; }
    }
}
