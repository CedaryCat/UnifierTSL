using System.Runtime.CompilerServices;
using Terraria;
using Terraria.IO;
using TrProtocol;
using UnifiedServerProcess;
using UnifierTSL.CLI;
using UnifierTSL.Events.Handlers;
using UnifierTSL.Network;

namespace UnifierTSL.Servers
{
    public partial class ServerContext : RootContext
    {
        public readonly Guid UniqueId = Guid.NewGuid();
        public readonly IWorldDataProvider worldDataProvider;
        public readonly ClientPacketReciever PacketReciever;

        public bool IsRunning;
        public ServerContext(string serverName, IWorldDataProvider worldData) : base(serverName) {
            Console = UnifierApi.EventHub.Server.CreateConsoleService(this) ?? new ConsoleClientLauncher(this);
            PacketReciever = new ClientPacketReciever(this);

            worldDataProvider = worldData;
            worldData.ApplyMetadata(this);

            Main.maxNetPlayers = byte.MaxValue;
            Netplay.ListenPort = -1;
            Netplay.UseUPNP = true;
        }
        public override string ToString() => $"{{ Type:ServerContext, WorldName:\"{Name}\", Players:{Main.player.Count(p => p.active)} }}";
    }
}
