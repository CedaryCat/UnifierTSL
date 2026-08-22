using Terraria;
using Terraria.Localization;
using Terraria.Net.Sockets;

namespace UnifierTSL.Network
{
    public sealed class LocalClientSender(int clientId) : SocketSender(clientId)
    {
        public readonly int ID = clientId;
        internal readonly object RoutePublicationGate = new();
        public RemoteClient Client => UnifiedServerCoordinator.globalClients[ID];
        public sealed override ISocket Socket => Client.Socket;
        public sealed override void Kick(NetworkText reason, bool bg = false) {
            var client = Client;
            client.PendingTermination = true;
            client.PendingTerminationApproved = true;
            base.Kick(reason, bg);
        }
    }
}
