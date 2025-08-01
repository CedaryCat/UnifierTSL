using Terraria.IO;

namespace UnifierTSL.Servers
{
    public interface IWorldDataProvider
    {
        public string WorldName { get; }
        public string WorldFileName { get; }
        public WorldFileData ApplyMetadata(ServerContext server); 

        public static IWorldDataProvider GenerateOrLoadExisting(string worldName, int worldSize, int difficulty = 2, int worldEvil = 0, string seed = "")
            => new GenerateOrLoadProvider(worldName, worldSize, difficulty, worldEvil, seed);
        private class GenerateOrLoadProvider(string worldName, int worldSize, int difficulty = 2, int worldEvil = 0, string seed = "") : IWorldDataProvider
        {
            public string WorldName => worldName;
            public string WorldFileName => Path.GetFileName(Utilities.IO.GetWorldPathFromName(worldName, true));

            public WorldFileData ApplyMetadata(ServerContext server) {
                server.Main.worldName = worldName;
                var worldPath = Utilities.IO.GetWorldPathFromName(worldName, true);
                if (File.Exists(worldPath)) {
                    server.Main.ActiveWorldFileData = server.WorldFile.GetAllMetadata(worldPath, false);
                }
                else {
                    server.Main.autoGenFileLocation = worldPath;
                    server.Main.GameMode = difficulty;
                    server.Main.AutogenSeedName = seed;
                    server.WorldGen.WorldGenParam_Evil = worldEvil switch {
                        1 => 0,
                        2 => 1,
                        _ => -1,
                    };
                    switch (worldSize) {
                        case 1:
                            server.Main.maxTilesX = 4200;
                            server.Main.maxTilesY = 1200;
                            break;
                        case 2:
                            server.Main.maxTilesX = 6400;
                            server.Main.maxTilesY = 1800;
                            break;
                        case 3:
                        default:
                            server.Main.maxTilesX = 8400;
                            server.Main.maxTilesY = 2400;
                            break;
                    }
                    server.Main.autoGen = true;
                    server.Main.ActiveWorldFileData = server.WorldFile.CreateMetadata(worldName, false, difficulty);
                }

                return server.Main.ActiveWorldFileData;
            }
        }

        public static IWorldDataProvider FromBytes(string worldName, byte[] worldFileData) => new FromBytesProvider(worldName, worldFileData);
        private class FromBytesProvider(string worldName, byte[] worldFileData) : IWorldDataProvider
        {
            public string WorldName => worldName;
            public string WorldFileName => Path.GetFileName(Utilities.IO.GetWorldPathFromName(worldName, true));

            public WorldFileData ApplyMetadata(ServerContext server) {
                var worldPath = Utilities.IO.GetWorldPathFromName(worldName, true);
                File.WriteAllBytes(worldPath, worldFileData);
                server.Main.ActiveWorldFileData = server.WorldFile.GetAllMetadata(worldPath, false);
                return server.Main.ActiveWorldFileData;
            }
        }
    }
}
