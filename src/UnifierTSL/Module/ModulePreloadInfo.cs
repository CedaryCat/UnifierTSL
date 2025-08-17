using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnifierTSL.FileSystem;

namespace UnifierTSL.Module
{
    public record ModulePreloadInfo(FileSignature FileSignature, string ModuleName, bool IsCoreModule, bool SpecifiesDependencies, string? RequiresCoreModule)
    {
        public bool IsRequiredCoreModule => RequiresCoreModule != null;
    }
}
