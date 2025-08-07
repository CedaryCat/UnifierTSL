using Terraria;
using Terraria.GameContent.NetModules;
using Terraria.Net;
using TrProtocol.NetPackets.Modules;
using TShockAPI.Extension;
using UnifierTSL.Events.Handlers;
using NetCreativeUnlocksModule = TrProtocol.NetPackets.Modules.NetCreativeUnlocksModule;

namespace TShockAPI.Handlers.NetModules
{
	/// <summary>
	/// Handles creative unlock requests
	/// </summary>
	public class CreativeUnlocksHandler : IPacketHandler<NetCreativeUnlocksModule>
	{
        public void OnReceive(ref RecievePacketEvent<NetCreativeUnlocksModule> args) {
			var server = args.LocalReciever.Server;
			var player = args.GetTSPlayer();

            if (!server.Main.GameModeInfo.IsJourneyMode) {
                server.Log.Debug(
                    GetString($"NetModuleHandler received attempt to unlock sacrifice while not in journey mode from {player.Name}")
                );

                args.HandleMode = PacketHandleMode.Cancel;
                return;
            }

            //if ( != 0) {
            //    server.Log.Debug(
            //        GetString($"CreativeUnlocksHandler received non-vanilla unlock request. Random field value: {UnknownField} but should be 0 from {player.Name}")
            //    );

            //    args.HandleMode = PacketHandleMode.Cancel;
            //    return;
            //}

            if (!player.HasPermission(Permissions.journey_contributeresearch)) {
                player.SendErrorMessage(GetString("You do not have permission to contribute research."));
                args.HandleMode = PacketHandleMode.Cancel;
                return;
            }

            var totalSacrificed = TShock.ResearchDatastore.SacrificeItem(server.Main.worldID, args.Packet.ItemId, args.Packet.Count, player);

            var response = Terraria.GameContent.NetModules.NetCreativeUnlocksModule.SerializeItemSacrifice(server, args.Packet.ItemId, totalSacrificed);
            server.NetManager.Broadcast(response);
        }
    }
}
