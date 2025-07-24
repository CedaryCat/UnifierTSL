using System.Collections.Immutable;
using System.Net.Sockets;
using Terraria;
using Terraria.Net.Sockets;
using UnifierTSL.Events.Core;
using UnifierTSL.Servers;

namespace UnifierTSL.Events.Handlers
{
    public struct SwitchJoinServerEvent(Player player, RemoteClient client, ImmutableArray<ServerContext> servers) : IEventContent
    {
        public readonly Player Player = player;
        public readonly RemoteClient Client = client;
        public readonly ImmutableArray<ServerContext> Servers = servers;
        public ServerContext? JoinServer;
    }
    public struct CreateSocketEvent(TcpClient client) : IEventContent
    {
        public readonly TcpClient Client = client;
        public ISocket? Socket;
    }
    public class CoordinatorEventBridge
    {
        public CoordinatorEventBridge() {
            UnifiedServerCoordinator.SwitchJoinServer += static (player, client) => {
                var eventData = new SwitchJoinServerEvent(player, client, UnifiedServerCoordinator.Servers);
                SwitchJoinServer.Invoke(ref eventData);
                return eventData.JoinServer;
            };
            UnifiedServerCoordinator.CreateSocket += static (client) => {
                var eventData = new CreateSocketEvent(client);
                CreateSocket.Invoke(ref eventData);
                return eventData.Socket ?? new TcpSocket(client);
            };
        }
        public static readonly ValueEventNoCancelProvider<SwitchJoinServerEvent> SwitchJoinServer = new();
        public static readonly ValueEventNoCancelProvider<CreateSocketEvent> CreateSocket = new();
    }
}
