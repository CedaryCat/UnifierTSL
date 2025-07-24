using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Terraria;
using Terraria.Localization;

namespace UnifierTSL.Servers
{
    public partial class ServerContext
    {
        public virtual void OnPlayerRecieveForwardedMsg(int receiver, ServerContext otherServer, Player sender, string text) {
            if (!Main.player[receiver].active) {
                return;
            }
            ChatHelper.SendChatMessageToClientAs(
                255, 
                NetworkText.FromLiteral($"[Realm·{otherServer.Name}] <{sender.name}>: {text}"), 
                sender.ChatColor(),
                receiver);
        }
    }
}
