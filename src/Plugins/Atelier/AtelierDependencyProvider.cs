using UnifierTSL.Module;
using UnifierTSL.Module.Dependencies;

namespace Atelier
{
    public class AtelierDependencyProvider : IDependencyProvider
    {
        public IReadOnlyList<ModuleDependency> GetDependencies() {
            var asm = typeof(AtelierDependencyProvider).Assembly;
            return [
                new NugetDependency(asm, "Microsoft.CodeAnalysis.CSharp.Scripting", new Version(4, 14, 0)),
                new NugetDependency(asm, "Microsoft.CodeAnalysis.CSharp.Workspaces", new Version(4, 14, 0)),
                new NugetDependency(asm, "Microsoft.CodeAnalysis.Features", new Version(4, 14, 0)),
                new NugetDependency(asm, "Microsoft.CodeAnalysis.CSharp.Features", new Version(4, 14, 0)),
            ];
        }
    }
}
