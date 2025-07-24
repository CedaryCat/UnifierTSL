using Terraria.Localization;
using Terraria.Net.Sockets;

namespace UnifierTSL.Network
{
    public sealed class LocalClientSender(int clientId) : SocketSender()
    {
        public readonly int ID = clientId;
        public sealed override ISocket Socket => UnifiedServerCoordinator.globalClients[ID].Socket;
        public sealed override void Kick(NetworkText reason, bool bg = false) {
            var client = UnifiedServerCoordinator.globalClients[ID];
            client.PendingTermination = true;
            client.PendingTerminationApproved = true;
            base.Kick(reason, bg);
        }
    }
}
