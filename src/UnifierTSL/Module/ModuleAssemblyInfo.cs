using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using UnifierTSL.FileSystem;
using UnifierTSL.Module.Dependencies;

namespace UnifierTSL.Module
{

    public record ModuleAssemblyInfo(AssemblyLoadContext Context, Assembly Assembly, ImmutableArray<ModuleDependency> Dependencies, FileSignature Signature);
}
