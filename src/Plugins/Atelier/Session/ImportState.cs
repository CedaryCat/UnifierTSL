using System.Collections.Immutable;

namespace Atelier.Session
{
    internal sealed record ImportState(
        ImmutableArray<string> BaselineImports,
        ImmutableArray<string> UserImports,
        ImmutableArray<string> EffectiveImports,
        ImmutableArray<string> ReferencePaths)
    {
        public static ImportState CreateBaseline(ImmutableArray<string> baselineImports, ImmutableArray<string> referencePaths) {
            return new ImportState(baselineImports, [], baselineImports, referencePaths);
        }

        public ImportState ResetToBaseline() {
            return new ImportState(BaselineImports, [], BaselineImports, ReferencePaths);
        }
    }
}
