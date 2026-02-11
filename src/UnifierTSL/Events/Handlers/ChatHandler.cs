using Microsoft.Xna.Framework;
using OTAPI;
using System.Diagnostics.CodeAnalysis;
using Terraria.Chat.Commands;
using Terraria.Localization;
using UnifiedServerProcess;
using UnifierTSL.Events.Core;
using UnifierTSL.Extensions;
using UnifierTSL.Localization.Terraria;
using UnifierTSL.Servers;

namespace UnifierTSL.Events.Handlers
{
    public readonly struct MessageEvent(MessageSender sender, string rawText, string text) : IEventContent
    {
        public readonly MessageSender Sender = sender;
        public readonly string RawText = rawText;
        public readonly string Text = text;
    }
    public readonly record struct MessageSender(ServerContext? SourceServer, byte UserId)
    {
        public readonly bool IsServer => SourceServer is null || UserId == byte.MaxValue;
        [MemberNotNullWhen(true, nameof(SourceServer))]
        public readonly bool IsClient => SourceServer is not null && UserId != byte.MaxValue;
        public readonly void Chat(string message, Color color) {
            if (SourceServer is null) {
                Console.ForegroundColor = color.ToConsoleColor();
                Console.WriteLine(message);
                Console.ResetColor();
            }
            else if (UserId == byte.MaxValue) {
                SourceServer.Console.ForegroundColor = color.ToConsoleColor();
                SourceServer.Console.WriteLine(message);
                SourceServer.Console.ForegroundColor = ConsoleColor.Gray;
            }
            else {
                SourceServer.ChatHelper.SendChatMessageToClient(NetworkText.FromLiteral(message), color, UserId);
            }
        }
    }
    public struct ChatEvent(int player, string text, ServerContext server) : IPlayerEventContent
    {
        public int Who { get; } = player;
        public string Text = text;
        public ServerContext Server { get; } = server;
    }
    public class ChatHandler
    {
        private static int isReadingInput = 0;

        internal void KeepReadingInput() {
            if (Interlocked.CompareExchange(ref isReadingInput, 1, 0) != 0) {
                return;
            }

            while (true) {
                string input = Console.ReadLine() ?? "";
                try {
                    MessageEvent.Invoke(new MessageEvent(new(null, byte.MaxValue), input, input), out _);
                }
                catch {

                }
            }
        }
        public ChatHandler() {
            On.Terraria.Chat.Commands.SayChatCommand.ProcessIncomingMessage += ProcessIncomingMessage;
            On.Terraria.Chat.ChatCommandProcessor.ProcessIncomingMessage += ProcessIncomingMessage;
            On.OTAPI.HooksSystemContext.MainSystemContext.InvokeCommandProcess_string += ProcessConsoleMessage;
        }

        public readonly ValueEventProvider<ChatEvent> ChatEvent = new();
        public readonly ReadonlyEventProvider<MessageEvent> MessageEvent = new();

        private bool ProcessConsoleMessage(On.OTAPI.HooksSystemContext.MainSystemContext.orig_InvokeCommandProcess_string orig, HooksSystemContext.MainSystemContext self,string raw) {

            MessageEvent.Invoke(new MessageEvent(new(self.root.ToServer(), byte.MaxValue), raw, raw.ToLower()), out bool handled);
            if (handled) {
                return false;
            }

            return orig(self, raw);
        }

        private void ProcessIncomingMessage(On.Terraria.Chat.ChatCommandProcessor.orig_ProcessIncomingMessage orig, Terraria.Chat.ChatCommandProcessor self, RootContext root, Terraria.Chat.ChatMessage message, int clientId) {

            string text = message.Text;
            // Terraria now has chat commands on the client side.
            // These commands remove the commands prefix (e.g. /me /playing) and send the command id instead
            // In order for us to keep legacy code we must reverse this and get the prefix using the command id
            foreach (KeyValuePair<LocalizedText, Terraria.Chat.ChatCommandId> item in Terraria.UI.Chat.ChatManager.Commands._localizedCommands) {
                if (item.Value._name == message.CommandId._name) {
                    if (!string.IsNullOrEmpty(text)) {
                        text = EnglishLanguage.GetCommandPrefixByName(item.Value._name) + ' ' + text;
                    }
                    else {
                        text = EnglishLanguage.GetCommandPrefixByName(item.Value._name);
                    }
                    break;
                }
            }

            MessageEvent.Invoke(new MessageEvent(new(root.ToServer(), (byte)clientId), message.Text, text), out bool handled);
            if (handled) {
                return;
            }

            orig(self, root, message, clientId);
            return;
        }
        private void ProcessIncomingMessage(On.Terraria.Chat.Commands.SayChatCommand.orig_ProcessIncomingMessage orig, SayChatCommand self, RootContext root, string text, byte clientId) {

            ChatEvent data = new(clientId, text, root.ToServer());
            ChatEvent.Invoke(ref data, out bool handled);
            if (handled) {
                return;
            }

            orig(self, root, data.Text, clientId);

            Terraria.Player player = root.Main.player[clientId];

            for (int i = 0; i < UnifiedServerCoordinator.globalClients.Length; i++) {
                if (!UnifiedServerCoordinator.globalClients[i].IsActive) {
                    continue;
                }
                if (i == clientId) {
                    continue;
                }
                ServerContext? otherServer = UnifiedServerCoordinator.GetClientCurrentlyServer(i);
                otherServer?.OnPlayerRecieveForwardedMsg(i, root.ToServer(), player, text);
            }
        }
    }
}
