﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TrProtocol.NetPackets;
using TShockAPI.Extension;
using UnifierTSL.Events.Handlers;

namespace TShockAPI.Handlers
{
    public class SyncTilePickingHandler : IPacketHandler<SyncTilePicking>
    {
        public void OnReceive(ref RecievePacketEvent<SyncTilePicking> args) {
            var server = args.LocalReciever.Server;
            var pos = args.Packet.Position;
            if (pos.X > server.Main.maxTilesX || pos.X < 0
               || pos.Y > server.Main.maxTilesY || pos.Y < 0) {
                server.Log.Debug(GetString($"SyncTilePickingHandler: X and Y position is out of world bounds! - From {args.GetTSPlayer().Name}"));
                args.HandleMode = PacketHandleMode.Cancel;
                return;
            }
        }
    }
}
