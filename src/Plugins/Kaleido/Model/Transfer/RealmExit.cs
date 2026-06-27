using System.Collections.Immutable;

namespace Kaleido.Model.Transfer
{
    public sealed record RealmExit(
        string? Reason = null,
        ImmutableDictionary<string, object?>? Metadata = null)
    {
        public static RealmExit Default { get; } = new();
    }
}
