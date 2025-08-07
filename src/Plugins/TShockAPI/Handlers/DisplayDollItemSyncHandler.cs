using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Terraria.DataStructures;
using Terraria.GameContent.Tile_Entities;
using TrProtocol.NetPackets;
using TShockAPI.Extension;
using UnifierTSL.Events.Handlers;

namespace TShockAPI.Handlers
{
    internal class DisplayDollItemSyncHandler : IPacketHandler<TEDisplayDollItemSync>
    {
        public void OnReceive(ref RecievePacketEvent<TEDisplayDollItemSync> args) {
            var player = args.GetTSPlayer();
            var server = args.LocalReciever.Server;

            if (!server.TileEntity.ByID.TryGetValue(args.Packet.TileEntityID, out var tileEntity) || tileEntity is not TEDisplayDoll displayDoll)
                return;
            /// If the player has no building permissions means that they couldn't even see the content of the doll in the first place.
            /// Thus, they would not be able to modify its content. This means that a hacker attempted to send this packet directly, or through raw bytes to tamper with the DisplayDoll. This is why I do not bother with making sure the player gets their item back.
            if (!player.HasBuildPermission(displayDoll.Position.X, displayDoll.Position.Y, false)) {
                player.SendErrorMessage(GetString("You do not have permission to modify a Mannequin in a protected area!"));
                args.HandleMode = PacketHandleMode.Cancel;
                return;
            }
        }
    }
}
