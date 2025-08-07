using System.IO;
using TrProtocol.NetPackets.Modules;
using UnifierTSL.Events.Handlers;

namespace TShockAPI.Handlers.NetModules
{
	/// <summary>
	/// Rejects ambience new modules from clients
	/// </summary>
	public class AmbienceHandler : IPacketHandler<NetAmbienceModule>
	{
        public void OnReceive(ref RecievePacketEvent<NetAmbienceModule> args) {
			args.HandleMode = PacketHandleMode.Cancel;
        }
    }
}
