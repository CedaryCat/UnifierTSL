using Kaleido.Systems;

namespace Kaleido.Runtime
{
    internal sealed class InstanceRetiringRegistration(
        RealmOrchestrator owner,
        string systemId,
        RealmEventHandler<RealmInstanceRetiring> handler,
        long sequence) : IDisposable
    {
        private int disposed;

        public string SystemId { get; } = systemId;
        public RealmEventHandler<RealmInstanceRetiring> Handler { get; } = handler;
        public long Sequence { get; } = sequence;

        public void Dispose() {
            if (Interlocked.Exchange(ref disposed, 1) == 0) {
                owner.Unregister(this);
            }
        }
    }
}
