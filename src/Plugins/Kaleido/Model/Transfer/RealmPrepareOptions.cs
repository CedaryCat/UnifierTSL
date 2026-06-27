namespace Kaleido.Model.Transfer
{
    public sealed record RealmPrepareOptions(TimeSpan? HoldFor = null, bool WaitUntilReady = true)
    {
        public static RealmPrepareOptions Default { get; } = new();
    }
}
