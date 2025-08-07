using MySqlX.XDevAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Terraria;
using TShockAPI.Configuration;
using TShockAPI.DB;
using UnifierTSL;
using UnifierTSL.Network;
using UnifierTSL.Servers;

namespace TShockAPI
{
    public partial class TSPlayer
    {
        public Player TPlayer => FakePlayer ?? UnifiedServerCoordinator.GetPllayer(Index);
        public bool RealPlayer {
            get { return Index >= 0 && Index < Main.maxPlayers && TPlayer != null; }
        }
        public virtual ServerContext GetCurrentServer() => UnifiedServerCoordinator.GetClientCurrentlyServer(Index)!;
        public RemoteClient Client => UnifiedServerCoordinator.globalClients[Index];
        public TShockSettings GetCurrentSettings() => TShock.Config.GetServerSettings(GetCurrentServer().Name);
        public LocalClientSender MsgSender => UnifiedServerCoordinator.clientSenders[Index];
    }
}
