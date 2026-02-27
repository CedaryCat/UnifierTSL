using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TrProtocol;
using UnifierTSL.Events.Handlers;

namespace TShockAPI.Handlers
{
    public interface IPacketHandler<TPacket> where TPacket : struct, INetPacket
    {
        void OnReceive(ref ReceivePacketEvent<TPacket> args);
    }
}
