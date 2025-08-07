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
    public partial class ServerContext : RootContext, ILoggerHost, ILogMetadataInjector
    {
        public readonly Guid UniqueId = Guid.NewGuid();
        public readonly IWorldDataProvider worldDataProvider;
        public readonly ClientPacketReciever PacketReciever;
        public readonly RoleLogger Log;

        public bool IsRunning;

        public string? CurrentLogCategory { get; set; }
        string ILoggerHost.Name => $"Log";

        public ServerContext(string serverName, IWorldDataProvider worldData, Logger? overrideLogCore = null) : base(serverName) {
            Console = UnifierApi.EventHub.Server.CreateConsoleService(this) ?? new ConsoleClientLauncher(this);
            PacketReciever = new ClientPacketReciever(this);
            Log = UnifierApi.CreateLogger(this, overrideLogCore);
            Log.AddMetadataInjector(injector: this);

            worldDataProvider = worldData;
            worldData.ApplyMetadata(this);

            Main.maxNetPlayers = byte.MaxValue;
            Netplay.ListenPort = -1;
            Netplay.UseUPNP = true;

            InitializeExtension();
        }

        public override string ToString() => $"{{ Type:ServerContext, WorldName:\"{Name}\", Players:{Main.player.Count(p => p.active)} }}";

        public void InjectMetadata(scoped ref LogEntry entry) {
            entry.SetMetadata("ServerContext", Name);
        }
    }
}
