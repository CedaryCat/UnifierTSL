using System.Collections.Immutable;

namespace Kaleido.Model.Transfer
{
    public sealed record RealmEntry(
        string? Anchor = null,
        ImmutableDictionary<string, object?>? Metadata = null)
    {
        public static RealmEntry Default { get; } = new();
    }
}
