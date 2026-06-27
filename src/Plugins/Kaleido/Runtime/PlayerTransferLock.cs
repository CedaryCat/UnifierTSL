namespace Kaleido.Runtime
{
    internal sealed class PlayerTransferLock
    {
        private readonly Dictionary<int, Entry> entries = [];
        private readonly Lock gate = new();

        public async ValueTask<IDisposable> EnterAsync(int playerId, CancellationToken cancellationToken) {
            Entry entry;
            lock (gate) {
                if (!entries.TryGetValue(playerId, out entry!)) {
                    entry = new();
                    entries.Add(playerId, entry);
                }

                entry.Users++;
            }

            try {
                await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                return new Lease(this, playerId, entry);
            }
            catch {
                ReleaseReference(playerId, entry);
                throw;
            }
        }

        private void Exit(int playerId, Entry entry) {
            entry.Semaphore.Release();
            ReleaseReference(playerId, entry);
        }

        private void ReleaseReference(int playerId, Entry entry) {
            lock (gate) {
                entry.Users--;
                if (entry.Users == 0
                    && entries.TryGetValue(playerId, out var current)
                    && ReferenceEquals(current, entry)) {
                    entries.Remove(playerId);
                    entry.Semaphore.Dispose();
                }
            }
        }

        private sealed class Entry
        {
            public SemaphoreSlim Semaphore { get; } = new(1, 1);
            public int Users { get; set; }
        }

        private sealed class Lease(PlayerTransferLock owner, int playerId, Entry entry) : IDisposable
        {
            private int disposed;

            public void Dispose() {
                if (Interlocked.Exchange(ref disposed, 1) == 0) {
                    owner.Exit(playerId, entry);
                }
            }
        }
    }
}
