using Terraria;
using UnifierTSL.Servers;

namespace Kaleido.Hosting
{
    public interface IVirtualTileWorldDataProvider : IWorldDataProvider
    {
        TileCollection CreateTileProvider(global::UnifierTSL.Servers.ServerContext server);

        WorldSaveMode IWorldDataProvider.SaveMode => WorldSaveMode.Suppress;
        WorldRuntimeOptions IWorldDataProvider.RuntimeOptions => new(SuppressLiquidUpdates: true);

        void IWorldDataProvider.ConfigureRuntime(global::UnifierTSL.Servers.ServerContext server) {
            server.Main.tile = CreateTileProvider(server);
        }
    }
}
