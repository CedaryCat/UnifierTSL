using TrProtocol.NetPackets;
using TShockAPI.Extension;
using UnifierTSL.Events.Handlers;

namespace TShockAPI.Handlers
{
	/// <summary>
	/// Handles emoji packets and checks for permissions
	/// </summary>
	public class EmojiHandler : IPacketHandler<Emoji>
	{
        public void OnReceive(ref ReceivePacketEvent<Emoji> args) {
			var player = args.GetTSPlayer();
            var server = args.LocalReceiver.Server;

            if (player.Index != args.Packet.PlayerSlot) {
                server.Log.Error(GetString($"IllegalPerSe: Emoji packet rejected for ID spoofing. Expected {player.Index}, received {args.Packet.PlayerSlot} from {player.Name}."));
                args.HandleMode = PacketHandleMode.Cancel;
                return;
            }

            if (!player.HasPermission(Permissions.sendemoji)) {
                player.SendErrorMessage(GetString("You do not have permission to send emotes!"));
                args.HandleMode = PacketHandleMode.Cancel;
                return;
            }
        }
    }
}
