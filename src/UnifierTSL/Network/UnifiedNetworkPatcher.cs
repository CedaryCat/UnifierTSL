using UnifierTSL.Servers;
using System.Net;
using Terraria;
using Terraria.Utilities;

namespace UnifierTSL.Network
{
    public static class UnifiedNetworkPatcher
    {
        static UnifiedNetworkPatcher() {
            On.Terraria.NetplaySystemContext.StartServer += Modified_StartServer;
            On.Terraria.NetplaySystemContext.StartBroadCasting += Modified_StartBroadCasting;
            On.Terraria.NetplaySystemContext.StopBroadCasting += Modified_StopBroadCasting;
        }

        private static void Modified_StartServer(On.Terraria.NetplaySystemContext.orig_StartServer orig, NetplaySystemContext self) {
            if (self.root is not ServerContext server) {
                orig(self);
                return;
            }
            self.Connection.ResetSpecialFlags();
            self.ResetNetDiag();

            var Main = server.Main;

            Main.rand ??= new UnifiedRandom((int)DateTime.Now.Ticks);
            Main.myPlayer = 255;
            self.ServerIP = IPAddress.Any;
            Main.menuMode = 14;
            Main.statusText = Lang.menu[8].Value;
            Main.netMode = 2;
            self.Disconnect = false;

            self.Clients = UnifiedServerCoordinator.globalClients;
            server.NetMessage.buffer = UnifiedServerCoordinator.globalMsgBuffers;
            server.IsRunning = true;
        }
        private static void Modified_StartBroadCasting(On.Terraria.NetplaySystemContext.orig_StartBroadCasting orig, NetplaySystemContext self) { }
        private static void Modified_StopBroadCasting(On.Terraria.NetplaySystemContext.orig_StopBroadCasting orig, NetplaySystemContext self) { }

        // Just triggers the static constructor
        public static void Load() { }
    }
}
