using Terraria;
using Terraria.IO;
using TrProtocol.NetPackets;
using UnifierTSL.Network;
using UnifierTSL.Servers;

namespace Kaleido.Hosting
{
    public interface IProjectionWorldDataProvider : IWorldDataProvider
    {
        int MaxTilesX { get; }
        int MaxTilesY { get; }
        int SpawnTileX { get; }
        int SpawnTileY { get; }
        double WorldSurface { get; }
        double RockLayer { get; }
        Guid UniqueId { get; }
        int WorldId { get; }
        TileCollection CreateTileProvider();
        WorldData CreateWorldDataPacket(global::UnifierTSL.Servers.ServerContext server);

        WorldSaveMode IWorldDataProvider.SaveMode => WorldSaveMode.Suppress;
        WorldRuntimeOptions IWorldDataProvider.RuntimeOptions => new(SuppressLiquidUpdates: true);
    }
}
