using Terraria.Localization;
using UnifiedServerProcess;
using UnifierTSL.Events.Core;
using UnifierTSL.Servers;

namespace UnifierTSL.Events.Handlers
{
    public struct ChatEvent(int player, string text, RootContext server) : IServerEventContent<RootContext>, IPlayerEventContent
    {
        public int Who { get; } = player;
        public string Text = text;
        public RootContext Server { get; } = server;
    }
    public class ChatHandler
    {
        public ChatHandler() {
            On.Terraria.Chat.Commands.SayChatCommand.ProcessIncomingMessage += ProcessIncomingMessage;
        }
        public readonly ValueEventProvider<ChatEvent> ChatEvent = new();

        private void ProcessIncomingMessage(On.Terraria.Chat.Commands.SayChatCommand.orig_ProcessIncomingMessage orig, Terraria.Chat.Commands.SayChatCommand self, RootContext root, string text, byte clientId) {
            var data = new ChatEvent(clientId, text, root);
            ChatEvent.Invoke(ref data, out var handled);
            if (handled) {
                return;
            }

            orig(self, root, data.Text, clientId);

            if (root is not ServerContext server) {
                return;
            }

            var player = root.Main.player[clientId];

            for (int i = 0; i < UnifiedServerCoordinator.globalClients.Length; i++) {
                if (!UnifiedServerCoordinator.globalClients[i].IsActive) {
                    continue;
                }
                var otherServer = UnifiedServerCoordinator.GetClientCurrentlyServer(i);
                otherServer?.OnPlayerRecieveForwardedMsg(i, server, player, text);
            }
        }
    }
}
