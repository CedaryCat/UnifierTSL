using Terraria;
using UnifiedServerProcess;
using UnifierTSL.Events.Core;
using UnifierTSL.Extensions;
using UnifierTSL.Network;
using UnifierTSL.Servers;

namespace UnifierTSL.Events.Handlers
{
    public class NetplayEventBridge
    {
        public readonly ReadonlyEventNoCancelProvider<LeaveEvent> LeaveEvent = new();
        public readonly ReadonlyEventNoCancelProvider<SocketResetEvent> SocketResetEvent = new();
        public readonly ReadonlyEventProvider<Connect> ConnectEvent = new();
        public readonly ReadonlyEventProvider<ReceiveFullClientInfo> ReceiveFullClientInfoEvent = new();
        public NetplayEventBridge() {
            On.Terraria.RemoteClient.Reset += RemoteClient_Reset;
        }

        private void RemoteClient_Reset(On.Terraria.RemoteClient.orig_Reset orig, RemoteClient client, RootContext root) {
            if (!root.Netplay.Disconnect) {
                if (client.IsActive) {
                    LeaveEvent leaveData = new(client.Id, root.ToServer());
                    LeaveEvent.Invoke(leaveData);
                }
                SocketResetEvent resetData = new(client.Id, client, root.ToServer());
                SocketResetEvent.Invoke(resetData);
            }
            orig(client, root);
        }
    }
    public readonly struct LeaveEvent(int plr, ServerContext server) : IPlayerEventContent
    {
        public int Who { get; } = plr;
        public ServerContext Server { get; init; } = server;
    }
    public readonly struct SocketResetEvent(int plr, RemoteClient socket, ServerContext server) : IPlayerEventContent
    {
        public int Who { get; } = plr;
        public readonly RemoteClient Socket = socket;
        public readonly ServerContext Server { get; init; } = server;
    }
    public readonly struct Connect(RemoteClient client, string version) : IEventContent
    {
        public readonly RemoteClient Client = client;
        public readonly string Version = version;
    }
    public readonly struct ReceiveFullClientInfo(RemoteClient client, Player player, LocalClientSender sender) : IEventContent
    {
        public readonly RemoteClient Client = client;
        public readonly Player Player = player;
        public readonly LocalClientSender Sender = sender;
    }
}
