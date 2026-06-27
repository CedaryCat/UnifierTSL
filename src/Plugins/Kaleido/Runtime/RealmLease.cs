namespace Kaleido.Runtime
{
    internal sealed class RealmLease(DateTimeOffset? expiresAtUtc)
    {
        public DateTimeOffset? ExpiresAtUtc { get; } = expiresAtUtc;
    }
}
