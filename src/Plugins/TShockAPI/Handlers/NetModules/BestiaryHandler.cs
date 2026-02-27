using System.IO;
using TrProtocol.NetPackets.Modules;
using UnifierTSL.Events.Handlers;

namespace TShockAPI.Handlers.NetModules
{
	/// <summary>
	/// Rejects client->server bestiary net modules as the client should never send this to the server
	/// </summary>
	public class BestiaryHandler : IPacketHandler<NetBestiaryModule>
    {
        public void OnReceive(ref ReceivePacketEvent<NetBestiaryModule> args) {
			args.HandleMode = PacketHandleMode.Cancel;
        }
    }
}
