using Terraria.DataStructures;
using Terraria.GameContent.Tile_Entities;
using TrProtocol.NetPackets;
using TShockAPI.Extension;
using UnifierTSL.Events.Handlers;

namespace TShockAPI.Handlers
{
    public class RequestTileEntityInteractionHandler : IPacketHandler<RequestTileEntityInteraction>
    {
        public void OnReceive(ref RecievePacketEvent<RequestTileEntityInteraction> args) {
            var player = args.GetTSPlayer();
            var server = args.LocalReciever.Server;
            var entityId = args.Packet.TileEntityID;

            if (!server.TileEntity.ByID.TryGetValue(entityId, out TileEntity? tileEntity))
                return;

            if (tileEntity is TEHatRack && !player.HasBuildPermissionForTileObject(tileEntity.Position.X, tileEntity.Position.Y, TEHatRack.entityTileWidth, TEHatRack.entityTileHeight, false)) {
                player.SendErrorMessage(GetString("You do not have permission to modify a Hat Rack in a protected area!"));
                args.HandleMode = PacketHandleMode.Cancel;
                return;
            }
            else if (tileEntity is TEDisplayDoll && !player.HasBuildPermissionForTileObject(tileEntity.Position.X, tileEntity.Position.Y, TEDisplayDoll.entityTileWidth, TEDisplayDoll.entityTileHeight, false)) {
                player.SendErrorMessage(GetString("You do not have permission to modify a Mannequin in a protected area!"));
                args.HandleMode = PacketHandleMode.Cancel;
                return;
            }
            else if (!player.HasBuildPermission(tileEntity.Position.X, tileEntity.Position.Y, false)) {
                player.SendErrorMessage(GetString("You do not have permission to modify a TileEntity in a protected area!"));
                server.Log.Debug(GetString($"RequestTileEntityInteractionHandler: Rejected packet due to lack of building permissions! - From {player.Name} | Position X:{tileEntity.Position.X} Y:{tileEntity.Position.Y}, TileEntity type: {tileEntity.type}, Tile type: {server.Main.tile[tileEntity.Position.X, tileEntity.Position.Y].type}"));
                args.HandleMode = PacketHandleMode.Cancel;
                return;
            }
        }
    }
}
