/*
TShock, a server mod for Terraria
Copyright (C) 2011-2019 Pryaxis & TShock Contributors

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/

using Microsoft.Xna.Framework;
using System.Text;
using Terraria;
using Terraria.Localization;
using TrProtocol.NetPackets.Modules;
using TShockAPI.DB;
using UnifierTSL;
using UnifierTSL.Servers;

namespace TShockAPI
{
    public class TSLauncherPlayer : TSPlayer
    {
        public static string AccountName = "UnifierTSL";
        public TSLauncherPlayer() : base("UnifierTSL") {
            Group = new SuperAdminGroup();
            Account = new UserAccount { Name = AccountName };
        }
        public sealed override ServerContext GetCurrentServer() {
            throw new NotImplementedException();
        }
        public override void SendErrorMessage(string msg) {
            SendConsoleMessage(msg, 255, 0, 0);
        }

        public override void SendInfoMessage(string msg) {
            SendConsoleMessage(msg, 255, 250, 170);
        }

        public override void SendSuccessMessage(string msg) {
            SendConsoleMessage(msg, 0, 255, 0);
        }

        public override void SendWarningMessage(string msg) {
            SendConsoleMessage(msg, 139, 0, 0);
        }

        public override void SendMessage(string msg, Color color) {
            SendMessage(msg, color.R, color.G, color.B);
        }

        public override void SendMessage(string msg, byte red, byte green, byte blue) {
            SendConsoleMessage(msg, red, green, blue);
        }

        public void BCErrorMessage(string msg) {
            BCMessage(msg, new Color(255, 0, 0));
        }

        public void BCInfoMessage(string msg) {
            BCMessage(msg, new Color(255, 250, 170));
        }

        public void BCSuccessMessage(string msg) {
            BCMessage(msg, new Color(0, 255, 0));
        }

        public void BCWarningMessage(string msg) {
			BCMessage(msg, new Color(139, 0, 0));
        }

        public void BCMessage(string msg, byte red, byte green, byte blue) {
            BCMessage(msg, new(red, green, blue));
        }
        public void BCMessage(string msg, Color color) {
            SendConsoleMessage(msg, color.R, color.G, color.B);
            var text = BuildBCPkt(msg, color);
            foreach (var sender in UnifiedServerCoordinator.clientSenders) {
                if (sender.Client.IsActive) {
                    sender.SendDynamicPacket_S(text);
                }
            }
        }

        static NetTextModule BuildBCPkt(string msg, Color color) {
            return new NetTextModule(null, new() {
                Color = color,
                PlayerSlot = Main.maxPlayers,
                Text = NetworkText.FromLiteral(msg)
            }, true);
        }

        public void SendConsoleMessage(string msg, byte red, byte green, byte blue) {
            /* var snippets = Terraria.UI.Chat.ChatManager.ParseMessage(Server, msg, new Color(red, green, blue));

            foreach (var snippet in snippets)
			{
                if (snippet.Color != default)
				{
                    Server.Console.ForegroundColor = PickNearbyConsoleColor(snippet.Color);
                }
                else
				{
                    Server.Console.ForegroundColor = ConsoleColor.Gray;
                }

                Server.Console.Write(snippet.Text);
            }
            Server.Console.WriteLine();
            Server.Console.ResetColor(); */

            var snippets = Terraria.UI.Chat.ChatManager.ParseMessage(TShock.ServerSample, msg, new Color(red, green, blue));

            var sb = new StringBuilder();

            foreach (var snippet in snippets) {
                if (!string.IsNullOrEmpty(snippet.Text)) {
                    if (snippet.Color != default) {
                        sb.Append(Utils.ColorTag(snippet.Text, snippet.Color));
                    }
                    else {
                        sb.Append(snippet.Text);
                    }
                }
            }

            UnifierApi.Logger.Info(msg);
        }
    }
}
