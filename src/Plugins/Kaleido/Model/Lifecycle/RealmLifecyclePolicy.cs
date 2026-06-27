namespace Kaleido.Model.Lifecycle
{
    public sealed record RealmLifecyclePolicy(RealmRetentionKind Retention, TimeSpan? EmptyDelay = null, TimeSpan? BufferTime = null)
    {
        public static RealmLifecyclePolicy Resident { get; } = new(RealmRetentionKind.Resident);
        public static RealmLifecyclePolicy UnloadWhenEmpty(TimeSpan? delay = null) => new(RealmRetentionKind.ElasticUnload, delay ?? TimeSpan.Zero);
        public static RealmLifecyclePolicy SuspendWhenEmpty(TimeSpan delay, TimeSpan? bufferTime = null) => new(RealmRetentionKind.ElasticSuspend, delay, bufferTime);
    }
}
