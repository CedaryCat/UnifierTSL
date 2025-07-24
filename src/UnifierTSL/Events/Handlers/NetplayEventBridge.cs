using Terraria;
using UnifiedServerProcess;
using UnifierTSL.Events.Core;

namespace UnifierTSL.Events.Handlers
{
    public class NetplayEventBridge
    {
        public readonly ReadonlyEventNoCancelProvider<LeaveEvent> LeaveEvent = new();
        public readonly ReadonlyEventNoCancelProvider<SocketResetEvent> SocketResetEvent = new();
        public NetplayEventBridge() {
            On.Terraria.RemoteClient.Reset += RemoteClient_Reset;
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
}
