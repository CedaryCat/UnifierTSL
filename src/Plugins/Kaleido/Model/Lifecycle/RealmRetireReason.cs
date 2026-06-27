namespace Kaleido.Model.Lifecycle
{
    public sealed record RealmRetireReason(RealmRetireKind Kind, string? Message = null)
    {
        public static RealmRetireReason Shutdown { get; } = new(RealmRetireKind.Shutdown);
        public static RealmRetireReason Empty { get; } = new(RealmRetireKind.Empty);
    }
}
