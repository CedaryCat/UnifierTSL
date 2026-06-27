using Kaleido.Systems;

namespace Kaleido.Runtime
{
    internal sealed class MaintenanceRegistration(
        RealmOrchestrator owner,
        string systemId,
        TimeSpan interval,
        RealmMaintenanceCallback callback) : IDisposable
    {
        private DateTimeOffset nextDueUtc = DateTimeOffset.UtcNow + interval;
        private int running;
        private int disposed;

        public string SystemId { get; } = systemId;

        public bool TryBegin(DateTimeOffset now) {
            if (Volatile.Read(ref disposed) != 0 || now < nextDueUtc || Interlocked.Exchange(ref running, 1) != 0) {
                return false;
            }

            nextDueUtc = now + interval;
            return true;
        }

        public ValueTask InvokeAsync(CancellationToken cancellationToken) => callback(cancellationToken);

        public void End() => Volatile.Write(ref running, 0);

        public void Dispose() {
            if (Interlocked.Exchange(ref disposed, 1) == 0) {
                owner.Unregister(this);
            }
        }
    }
}
