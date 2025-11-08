using Terraria;
using UnifiedServerProcess;
using UnifierTSL.CLI;
using UnifierTSL.Extensions;
using UnifierTSL.Logging;
using UnifierTSL.Network;

namespace UnifierTSL.Servers
{
    public partial class ServerContext : RootContext, IDisposable, ILoggerHost, ILogMetadataInjector
    {
        public readonly Guid UniqueId = Guid.NewGuid();
        public readonly IWorldDataProvider worldDataProvider;
        public readonly ClientPacketReciever PacketReciever;
        public readonly RoleLogger Log;

        public Thread? RunningThread { get; private set; }

        internal int ActivePlayers;
        public bool IsRunning { get; private set; }

        public string? CurrentLogCategory { get; set; }
        string ILoggerHost.Name => $"Log";

        internal static void Initialize() {
            On.Terraria.NetplaySystemContext.StartServer += StartServer;
            On.Terraria.Main.YouCanSleepNow += CloseServer;
            On.UnifiedServerProcess.RootContext.ctor += OnCreateInstance;
        }

        private static void CloseServer(On.Terraria.Main.orig_YouCanSleepNow orig, Main self, RootContext root) {
            orig(self, root);
            root.ToServer().IsRunning = false;
        }

        private static void StartServer(On.Terraria.NetplaySystemContext.orig_StartServer orig, Terraria.NetplaySystemContext self) {
            orig(self);
            self.root.ToServer().IsRunning = true;
        }

        private static void OnCreateInstance(On.UnifiedServerProcess.RootContext.orig_ctor orig, RootContext self, string name) {
            if (self is not ServerContext) {
                throw new InvalidOperationException(GetParticularString("{0} is class name (SampleServer)", $"Under the UnifierTSL API, only instances of ServerContext (or its derivatives) can be created. To create a sample context, use '{typeof(SampleServer).FullName}' instead."));
            }
            orig(self, name);
        }

        protected virtual ConsoleSystemContext CreateConsoleService()
            => UnifierApi.EventHub.Server.InvokeCreateConsoleService(this) ?? new ConsoleClientLauncher(this);

        public ServerContext(string serverName, IWorldDataProvider worldData, Logger? overrideLogCore = null) : base(serverName) {
            Console = CreateConsoleService();
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

        public virtual Thread Run(string[] args) {
            Thread result = RunningThread = new Thread(() => RunBlocking(args)) {
                IsBackground = true,
            };
            result.Name = $"Server Instance: {Name}";
            result.Start();
            return result;
        }
        public virtual void RunBlocking(string[] args) {
            RunningThread = Thread.CurrentThread;
            RunningThread.Name = $"Server Instance: {Name}";
            Program.LaunchGame(args);
        }

        public virtual Task Stop() {
            Netplay.Disconnect = true;
            return Task.Run(() => {
                if (RunningThread != null && RunningThread.IsAlive) {
                    RunningThread.Join();
                }
                RunningThread = null;
            });
        }

        public virtual Task Close() {
            Netplay.Disconnect = true;
            Task task = Task.Run(() => {
                if (RunningThread != null && RunningThread.IsAlive) {
                    RunningThread.Join();
                }
                RunningThread = null;
                Dispose();
            });
            return task;
        }

        public override string ToString() => $"{{ Type:ServerContext, WorldName:\"{Name}\", Players:{Main.player.Count(p => p.active)} }}";

        public void InjectMetadata(scoped ref LogEntry entry) {
            entry.SetMetadata("ServerContext", Name);
        }

        private bool disposedValue;
        protected virtual void Dispose(bool disposing) {
            Netplay.Disconnect = true;
            Console.Dispose();
            if (!disposedValue) {
                if (disposing) {
                    DisposeExtension();
                }

                disposedValue = true;
            }
        }

        // ~ServerContext()
        // {
        //     Dispose(disposing: false);
        // }

        public void Dispose() {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
