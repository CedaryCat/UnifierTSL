namespace Kaleido.Systems
{
    public sealed class RealmSystemLease : IAsyncDisposable
    {
        private readonly IRealmSystem system;
        private readonly RealmSystemScope scope;
        private readonly Action<RealmSystemLease> release;
        private int disposed;

        internal RealmSystemLease(IRealmSystem system, RealmSystemScope scope, Action<RealmSystemLease> release) {
            this.system = system;
            this.scope = scope;
            this.release = release;
            SystemId = scope.SystemId;
        }

        public string SystemId { get; }

        public async ValueTask DisposeAsync() {
            if (Interlocked.Exchange(ref disposed, 1) != 0) {
                return;
            }

            scope.Dispose();
            try {
                if (system is IAsyncDisposable asyncDisposable) {
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                }
                else if (system is IDisposable disposable) {
                    disposable.Dispose();
                }
            }
            finally {
                release(this);
            }
        }
    }
}
