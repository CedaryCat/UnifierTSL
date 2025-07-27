using System.Collections.Concurrent;
using System.Collections.Generic;
using Terraria;
using Terraria.Utilities;

namespace UnifierTSL
{
    public static class SynchronizedGuard
    {

        // Just trigger the static constructor

        public readonly static Lock ConsoleLock = new();
        public static void Load() { }

        static readonly Lock cultureFileLock = new();
        static readonly Lock creativeSacrificesLock = new();
        static readonly Lock favoritesFileLock = new();
        sealed class FileLockManager
        {
            sealed class FileLock
            {
                public readonly SemaphoreSlim Lock = new(1, 1);
                private int refCount;

                public void IncrementRef() => Interlocked.Increment(ref refCount);
                public int DecrementRef() => Interlocked.Decrement(ref refCount);
            }
            sealed class Releaser(string filePath, FileLockManager.FileLock fileLock) : IDisposable
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
        static SynchronizedGuard() {
            On.Terraria.GameContent.Creative.CreativeItemSacrificesCatalog.Initialize
                += (orig, self) 
                => { lock (creativeSacrificesLock) { orig(self); } };
            On.Terraria.IO.FavoritesFile.Save 
                += (orig, file, root)
                => { lock (favoritesFileLock) { orig(file, root); } };
            On.Terraria.IO.FavoritesFile.Load
                += (orig, file, root)
                => { lock (favoritesFileLock) { orig(file, root); } };
            On.Terraria.IO.WorldFileSystemContext.mfwh_SaveWorld_bool_bool
                += (orig, self, _, resetTime) 
                => {
                    var s = self.root;
                    if (s.Main.worldName == "") {
                        s.Main.worldName = "World";
                    }
                    while (s.WorldGen.IsGeneratingHardMode) {
                        s.Main.statusText = Lang.gen[48].Value;
                    }
                    using (FileLockManager.Enter(self.root.Main.worldPathName)) {
                        FileUtilities.ProtectedInvoke(delegate {
                            self.InternalSaveWorld(false, resetTime);
                        });
                    }
                };
        }
    }
}
