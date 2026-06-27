using Kaleido.Runtime;

namespace Kaleido.Model.Transfer
{
    public sealed class RealmHold : IDisposable
    {
        private readonly RealmRuntime runtime;
        private readonly RealmLease lease;
        private int disposed;

        internal RealmHold(RealmRuntime runtime, RealmLease lease) {
            this.runtime = runtime;
            this.lease = lease;
            ExpiresAtUtc = lease.ExpiresAtUtc;
        }

        public DateTimeOffset? ExpiresAtUtc { get; }

        public void Dispose() {
            if (Interlocked.Exchange(ref disposed, 1) == 0) {
                runtime.ReleaseHold(lease);
            }
        }
    }
}
