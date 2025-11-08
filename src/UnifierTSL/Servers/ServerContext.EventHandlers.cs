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
                NetworkText.FromLiteral(GetParticularString("{0} is server name, {1} is player name, {2} is chat message", $"[Realm·{otherServer.Name}] <{sender.name}>: {text}")),
                sender.ChatColor(),
                receiver);
        }
    }
}
