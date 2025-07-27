using System.Runtime.CompilerServices;
using Terraria;
using Terraria.IO;
using TrProtocol;
using UnifiedServerProcess;
using UnifierTSL.CLI;
using UnifierTSL.Events.Handlers;
using UnifierTSL.Logging;
using UnifierTSL.Network;

namespace UnifierTSL.Servers
{
    public partial class ServerContext : RootContext, ILoggerHost
    {
        public readonly Guid UniqueId = Guid.NewGuid();
        public readonly IWorldDataProvider worldDataProvider;
        public readonly ClientPacketReciever PacketReciever;
        public readonly RoleLogger Logger;

        public bool IsRunning;

        public string? CurrentLogCategory { get; set; }
        string ILoggerHost.Name => $"Serv:{Name}";

        public ServerContext(string serverName, IWorldDataProvider worldData, Logger? overrideLogCore = null) : base(serverName) {
            Console = UnifierApi.EventHub.Server.CreateConsoleService(this) ?? new ConsoleClientLauncher(this);
            PacketReciever = new ClientPacketReciever(this);
            Logger = UnifierApi.CreateLogger(this, overrideLogCore);

            worldDataProvider = worldData;
            worldData.ApplyMetadata(this);

            Main.maxNetPlayers = byte.MaxValue;
            Netplay.ListenPort = -1;
            Netplay.UseUPNP = true;
        }

        public override string ToString() => $"{{ Type:ServerContext, WorldName:\"{Name}\", Players:{Main.player.Count(p => p.active)} }}";
    }
}
