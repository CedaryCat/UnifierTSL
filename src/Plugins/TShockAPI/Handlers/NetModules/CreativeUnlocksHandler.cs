using Terraria;
using Terraria.GameContent.NetModules;
using Terraria.ID;
using Terraria.Net;
using TrProtocol.NetPackets.Modules;
using TShockAPI.Extension;
using UnifierTSL.Events.Handlers;
using NetCreativeUnlocksPlayerReportModule = TrProtocol.NetPackets.Modules.NetCreativeUnlocksPlayerReportModule;

namespace TShockAPI.Handlers.NetModules
{
	/// <summary>
	/// Handles creative unlock requests
	/// </summary>
public class CreativeUnlocksHandler : IPacketHandler<NetCreativeUnlocksPlayerReportModule>
	{
        public void OnReceive(ref RecievePacketEvent<NetCreativeUnlocksPlayerReportModule> args) {
			var server = args.LocalReciever.Server;
			var player = args.GetTSPlayer();

            if (server.Main.GameMode != GameModeID.Creative) {
                server.Log.Debug(
                    GetString($"NetModuleHandler received attempt to unlock sacrifice while not in journey mode from {player.Name}")
                );

                args.HandleMode = PacketHandleMode.Cancel;
                return;
            }

            if (args.Packet.PlayerSlot != player.Index) {
                server.Log.Debug(
                    GetString($"CreativeUnlocksHandler received spoofed player slot request. PlayerSlot: {args.Packet.PlayerSlot}, Sender: {player.Index} ({player.Name})")
                );

                args.HandleMode = PacketHandleMode.Cancel;
                return;
            }

            if (!player.HasPermission(Permissions.journey_contributeresearch)) {
                player.SendErrorMessage(GetString("You do not have permission to contribute research."));
                args.HandleMode = PacketHandleMode.Cancel;
                return;
            }

            var totalSacrificed = TShock.ResearchDatastore.SacrificeItem(server.Main.worldID, args.Packet.ItemId, args.Packet.Count, player);

            var response = Terraria.GameContent.NetModules.NetCreativeUnlocksPlayerReportModule.SerializeSacrificeRequest(server, player.Index, args.Packet.ItemId, totalSacrificed);
            server.NetManager.Broadcast(response);
        }
    }
}
