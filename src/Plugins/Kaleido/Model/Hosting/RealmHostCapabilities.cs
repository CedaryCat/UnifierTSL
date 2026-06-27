namespace Kaleido.Model.Hosting
{
    public sealed record RealmHostCapabilities(
        RealmHostKind Kind,
        bool HasServerContext,
        bool HasProjection,
        bool HasRealEntities,
        bool SupportsMultiplePlayers,
        bool SupportsUnload);
}
