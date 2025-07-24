using Terraria.Net.Sockets;

namespace UnifierTSL.Network
{
    public sealed class TmpSocketSender(ISocket socket) : SocketSender()
    {
        public sealed override ISocket Socket => socket;
    }
}
