using Terraria.GameContent.NetModules;
using TShockAPI.Extension;
using UnifierTSL.Events.Handlers;
using NetTeleportPylonModule = TrProtocol.NetPackets.Modules.NetTeleportPylonModule;

namespace TShockAPI.Handlers.NetModules
{
	/// <summary>
	/// Handles a pylon net module
	/// </summary>
	public class PylonHandler : IPacketHandler<NetTeleportPylonModule>
	{
        public void OnReceive(ref RecievePacketEvent<NetTeleportPylonModule> args) {
			if (args.Packet.PylonPacketType == NetTeleportPylonModule_SubPacketType.PlayerRequestsTeleport) {
				var player = args.GetTSPlayer();

                if (!player.HasPermission(Permissions.pylon)) {
					args.HandleMode = PacketHandleMode.Cancel;
                    player.SendErrorMessage(GetString("You do not have permission to teleport using pylons."));
                    return;
                }
            }
        }
    }
}
