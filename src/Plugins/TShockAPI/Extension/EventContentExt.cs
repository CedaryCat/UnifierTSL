using TrProtocol;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnifierTSL.Events.Handlers;

namespace TShockAPI.Extension
{
    public static class EventContentExt
    {
        public static TSPlayer GetTSPlayer<TPacket>(this in ReceivePacketEvent<TPacket> args) where TPacket : struct, INetPacket {
            return TShock.Players[args.Who];
        }
    }
}
