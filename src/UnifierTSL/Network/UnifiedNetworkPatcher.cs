using System.Net;
using Terraria;
using UnifierTSL.Extensions;
using UnifierTSL.Servers;

namespace UnifierTSL.Network
{
    public class UnifiedNetworkPatcher
    {
        static UnifiedNetworkPatcher() {
            On.Terraria.NetplaySystemContext.StartServer += Modified_StartServer;
            On.Terraria.NetplaySystemContext.StartBroadCasting += Modified_StartBroadCasting;
            On.Terraria.NetplaySystemContext.StopBroadCasting += Modified_StopBroadCasting;
        }

        private static void Modified_StartServer(On.Terraria.NetplaySystemContext.orig_StartServer orig, NetplaySystemContext self) {
            ServerContext server = self.root.ToServer();
            self.Connection.ResetSpecialFlags();
            self.ResetNetDiag();

            MainSystemContext Main = server.Main;
            self.ServerIP = IPAddress.Any;
            Main.menuMode = 14;
            Main.statusText = Lang.menu[8].Value;
            self.Disconnect = false;

            self.Clients = UnifiedServerCoordinator.globalClients;
            server.NetMessage.buffer = UnifiedServerCoordinator.globalMsgBuffers;
        }
        private static void Modified_StartBroadCasting(On.Terraria.NetplaySystemContext.orig_StartBroadCasting orig, NetplaySystemContext self) { }
        private static void Modified_StopBroadCasting(On.Terraria.NetplaySystemContext.orig_StopBroadCasting orig, NetplaySystemContext self) { }

        // Just triggers the static constructor
        public static void Load() { }
    }
}
