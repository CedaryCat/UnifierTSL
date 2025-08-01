using System.Collections.Generic;
using Terraria;
using Terraria.Utilities;
using UnifierTSL.FileSystem;

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
