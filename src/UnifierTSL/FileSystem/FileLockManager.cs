using System.Collections.Concurrent;

namespace UnifierTSL.FileSystem
{
    public sealed class FileLockManager
    {
        sealed class FileLock
        {
            public readonly SemaphoreSlim Lock = new(1, 1);
            private int refCount;

            public void IncrementRef() => Interlocked.Increment(ref refCount);
            public int DecrementRef() => Interlocked.Decrement(ref refCount);
        }
        sealed class Releaser(string filePath, FileLock fileLock) : IDisposable
        {
            private readonly string filePath = filePath;
            private readonly FileLock fileLock = fileLock;
            private bool disposed;

            public void Dispose() {
                if (disposed) return;
                fileLock.Lock.Release();
                if (fileLock.DecrementRef() == 0) {
                    locks.TryRemove(filePath, out _);
                }
                disposed = true;
            }
        }
        static readonly ConcurrentDictionary<string, FileLock> locks = new();
        public static IDisposable Enter(string filePath) {
            var fileLock = locks.GetOrAdd(filePath, _ => new FileLock());
            fileLock.IncrementRef();
            fileLock.Lock.Wait();

            return new Releaser(filePath, fileLock);
        }
    }
}
