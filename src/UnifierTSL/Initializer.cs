using ReLogic.OS;
using Terraria.ID;
using UnifiedServerProcess;
using UnifierTSL.Network;

namespace UnifierTSL
{
    public static class Initializer
    {
        static Initializer() {
            AssemblyResolverInit();
        }

        private static void AssemblyResolverInit() {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveHelpers.GlobalResolveAssembly;
            System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += ResolveHelpers.ResolveAssembly;
        }

        /// <summary>
        /// Just triggers the static constructor
        /// </summary>
        public static void InitializeResolver() { }

        public static void Initialize() {
            Terraria.Program.SavePath = Platform.Get<IPathService>().GetStoragePath("Terraria");
            Terraria.Main.SkipAssemblyLoad = true;
            GlobalInitializer.Initialize();
            SynchronizedGuard.Load();
            UnifiedNetworkPatcher.Load();
            InitSets();
        }


        private static void InitSets() {
            ItemID.Sets.Explosives = ItemID.Sets.Factory.CreateBoolSet([
				// Bombs
				ItemID.Bomb,
                ItemID.StickyBomb,
                ItemID.BouncyBomb,
                ItemID.BombFish,
                ItemID.DirtBomb,
                ItemID.DirtStickyBomb,
                ItemID.ScarabBomb,
				// Launchers
				ItemID.GrenadeLauncher,
                ItemID.RocketLauncher,
                ItemID.SnowmanCannon,
                ItemID.Celeb2,
				// Rockets
				ItemID.RocketII,
                ItemID.RocketIV,
                ItemID.ClusterRocketII,
                ItemID.MiniNukeII,
				// The following are classified as explosives untill we can figure out a better way.
				ItemID.DryRocket,
                ItemID.WetRocket,
                ItemID.LavaRocket,
                ItemID.HoneyRocket,
				// Explosives & misc
				ItemID.Dynamite,
                ItemID.Explosives,
                ItemID.StickyDynamite
            ]);

            //Set corrupt tiles to true, as they aren't in vanilla
            TileID.Sets.Corrupt[TileID.CorruptGrass] = true;
            TileID.Sets.Corrupt[TileID.CorruptPlants] = true;
            TileID.Sets.Corrupt[TileID.CorruptThorns] = true;
            TileID.Sets.Corrupt[TileID.CorruptIce] = true;
            TileID.Sets.Corrupt[TileID.CorruptHardenedSand] = true;
            TileID.Sets.Corrupt[TileID.CorruptSandstone] = true;
            TileID.Sets.Corrupt[TileID.Ebonstone] = true;
            TileID.Sets.Corrupt[TileID.Ebonsand] = true;

            //Same again for crimson
            TileID.Sets.Crimson[TileID.FleshBlock] = true;
            TileID.Sets.Crimson[TileID.CrimsonGrass] = true;
            TileID.Sets.Crimson[TileID.FleshIce] = true;
            TileID.Sets.Crimson[TileID.CrimsonPlants] = true;
            TileID.Sets.Crimson[TileID.Crimstone] = true;
            TileID.Sets.Crimson[TileID.Crimsand] = true;
            TileID.Sets.Crimson[TileID.CrimsonVines] = true;
            TileID.Sets.Crimson[TileID.CrimsonThorns] = true;
            TileID.Sets.Crimson[TileID.CrimsonHardenedSand] = true;
            TileID.Sets.Crimson[TileID.CrimsonSandstone] = true;

            //And hallow
            TileID.Sets.Hallow[TileID.HallowedGrass] = true;
            TileID.Sets.Hallow[TileID.HallowedPlants] = true;
            TileID.Sets.Hallow[TileID.HallowedPlants2] = true;
            TileID.Sets.Hallow[TileID.HallowedVines] = true;
            TileID.Sets.Hallow[TileID.HallowedIce] = true;
            TileID.Sets.Hallow[TileID.HallowHardenedSand] = true;
            TileID.Sets.Hallow[TileID.HallowSandstone] = true;
            TileID.Sets.Hallow[TileID.Pearlsand] = true;
            TileID.Sets.Hallow[TileID.Pearlstone] = true;
        }
    }
}
