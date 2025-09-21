using UnifierTSL.FileSystem;

namespace UnifierTSL.Module
{
    public record ModulePreloadInfo(FileSignature FileSignature, string ModuleName, bool IsCoreModule, bool SpecifiesDependencies, string? RequiresCoreModule)
    {
        public bool IsRequiredCoreModule => RequiresCoreModule != null;
    }
}
