using System.Diagnostics.CodeAnalysis;
using Terraria;
using Terraria.Localization;
using UnifierTSL.Events.Handlers;

namespace UnifierTSL.Servers
{
    public partial class ServerContext
    {
        public virtual void OnPlayerReceiveForwardedMsg(int receiver, ServerContext otherServer, Player sender, string text) {
            if (!Main.player[receiver].active) {
                return;
            }
            ChatHelper.SendChatMessageToClientAs(
                255,
                NetworkText.FromLiteral(GetParticularString("{0} is server name, {1} is player name, {2} is chat message", $"[Realm·{otherServer.Name}] <{sender.name}>: {text}")),
                sender.ChatColor(),
                receiver);
        }
        public virtual bool CheckPlayerCanJoinIn(Player player, RemoteClient client, [NotNullWhen(false)] out NetworkText? failReason) {
            var data = new ServerCheckPlayerCanJoinIn(player, client, this);
            UnifierApi.EventHub.Coordinator.ServerCheckPlayerCanJoinIn.Invoke(ref data);
            if (!data.CanJoin) {
                failReason = data.FailReason ?? NetworkText.Empty;
                return false;
            }
            if (player.difficulty == 3 && !Main.IsJourneyMode) {
                failReason = NetworkText.FromKey("Net.PlayerIsCreativeAndWorldIsNotCreative");
                return false;
            }
            else if (player.difficulty != 3 && Main.IsJourneyMode) {
                failReason = NetworkText.FromKey("Net.PlayerIsNotCreativeAndWorldIsCreative");
                return false;
            }
            failReason = null;
            return true;
        }
    }
}
