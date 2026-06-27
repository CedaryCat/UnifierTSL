using Kaleido.Systems;

namespace Kaleido.Runtime
{
    internal sealed class JoinRegistration(
        RealmOrchestrator owner,
        string systemId,
        RealmJoinHandler handler,
        int priority,
        long sequence) : IDisposable
    {
        private int disposed;

        public string SystemId { get; } = systemId;
        public RealmJoinHandler Handler { get; } = handler;
        public int Priority { get; } = priority;
        public long Sequence { get; } = sequence;

        public void Dispose() {
            if (Interlocked.Exchange(ref disposed, 1) == 0) {
                owner.Unregister(this);
            }
        }
    }
}
