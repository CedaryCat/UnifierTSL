using System.IO;
using TrProtocol.NetPackets.Modules;
using UnifierTSL.Events.Handlers;

namespace TShockAPI.Handlers.NetModules
{
	/// <summary>
	/// Handles the NetLiquidModule. Rejects all incoming net liquid requests, as clients should never send them
	/// </summary>
	public class LiquidHandler : IPacketHandler<NetLiquidModule>
	{
        public void OnReceive(ref RecievePacketEvent<NetLiquidModule> args) {
			args.HandleMode = PacketHandleMode.Cancel;
        }
    }
}
