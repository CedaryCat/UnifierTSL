namespace Kaleido.Model.Hosting
{
    public sealed record RealmHostRequirement(
        RealmHostKind? Kind = null,
        bool NeedsServerContext = false,
        bool NeedsProjection = false,
        bool NeedsRealEntities = false,
        bool NeedsMultiplePlayers = false)
    {
        public static RealmHostRequirement SharedProjection { get; } = new(Kind: RealmHostKind.SharedProjection);
        public static RealmHostRequirement ServerContext { get; } = new(Kind: RealmHostKind.ServerContext, NeedsServerContext: true, NeedsRealEntities: true, NeedsMultiplePlayers: true);

        public bool IsSatisfiedBy(RealmHostCapabilities capabilities) {
            return (!Kind.HasValue || capabilities.Kind == Kind.Value)
                && (!NeedsServerContext || capabilities.HasServerContext)
                && (!NeedsProjection || capabilities.HasProjection)
                && (!NeedsRealEntities || capabilities.HasRealEntities)
                && (!NeedsMultiplePlayers || capabilities.SupportsMultiplePlayers);
        }
    }
}
