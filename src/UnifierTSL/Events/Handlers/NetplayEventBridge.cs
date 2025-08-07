using System.Net;
using Terraria;
using UnifiedServerProcess;
using UnifierTSL.Events.Core;
using UnifierTSL.Network;

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
            UnifiedServerCoordinator.HandleConnect += UnifiedServerCoordinator_HandleConnect;
            UnifiedServerCoordinator.HandleJoin += UnifiedServerCoordinator_HandleJoin;
        }

        private bool UnifiedServerCoordinator_HandleJoin(Player arg1, RemoteClient arg2, LocalClientSender arg3) {
            var data = new ReceiveFullClientInfo(arg2, arg1, arg3);
            ReceiveFullClientInfoEvent.Invoke(in data, out var handled);
            return handled;
        }

        private bool UnifiedServerCoordinator_HandleConnect(RemoteClient arg1, string arg2) {
            var data = new Connect(arg1, arg2);
            ConnectEvent.Invoke(in data, out var handled);
            return handled;
        }

        private void RemoteClient_Reset(On.Terraria.RemoteClient.orig_Reset orig, RemoteClient client, RootContext root) {
            if (!root.Netplay.Disconnect) {
                if (client.IsActive) {
                    var leaveData = new LeaveEvent(client.Id, root);
                    LeaveEvent.Invoke(leaveData);
                }
                var resetData = new SocketResetEvent(client.Id, client, root);
                SocketResetEvent.Invoke(resetData);
            }
            orig(client, root);
        }
    }
    public readonly struct LeaveEvent(int plr, RootContext server) : IPlayerEventContent {
        public int Who { get; } = plr;
        public readonly RootContext Server = server;
    }
    public readonly struct SocketResetEvent(int plr, RemoteClient socket, RootContext server) : IPlayerEventContent
    {
        public int Who { get; } = plr;
        public readonly RemoteClient Socket = socket;
        public readonly RootContext Server = server;
    }
    public readonly struct Connect(RemoteClient client, string version) : IEventContent
    {
        public readonly RemoteClient Client = client;
        public readonly string Version = version;
    }
    public readonly struct ReceiveFullClientInfo(RemoteClient client, Player player, LocalClientSender sender) : IEventContent
    {
        public readonly RemoteClient Client = client;
    }
}
