using LinqToDB.Data;
using MaxMind;
using Rests;
using System.Collections.Immutable;
using System.Net;
using System.Reflection;
using Terraria;
using TShockAPI.CLI;
using TShockAPI.Configuration;
using TShockAPI.DB;
using TShockAPI.Modules;
using TShockAPI.Sockets;
using UnifiedServerProcess;
using UnifierTSL;
using UnifierTSL.Events.Core;
using UnifierTSL.Events.Handlers;
using UnifierTSL.Logging;
using UnifierTSL.Plugins;
using UnifierTSL.Servers;

namespace TShockAPI
{
    [PluginMetadata("TShock", "5.2.2", "The TShock Team", "The administration modification of the future.")]
    public class TShock : BasePlugin
    {
        class LogHost : ILoggerHost {
            string ILoggerHost.Name => "TShock";
            string? ILoggerHost.CurrentLogCategory => null;
        }

        public static RoleLogger Log = UnifierApi.CreateLogger(new LogHost());

        #region Fields
        /// <summary>VersionNum - The version number the TerrariaAPI will return back to the API. We just use the Assembly info.</summary>
        public static readonly Version VersionNum = Assembly.GetExecutingAssembly().GetName().Version;
        /// <summary>VersionCodename - The version codename is displayed when the server starts. Inspired by software codenames conventions.</summary>
        public static readonly string VersionCodename = "Hopefully SSC works somewhat correctly now edition";
        public static string SavePath { get; private set; } = null!;
        #endregion

#nullable disable

        #region Configuration
        public static TShockConfig Config;
        public static ServerSideConfig ServerSideCharacterConfig;
        #endregion

        #region DB
        public static DataConnection DB;
        #endregion

        #region DataManager
        public static BanManager Bans;
        /// <summary>Warps - Static reference to the warp manager for accessing the warp system.</summary>
        public static WarpManager Warps;
        /// <summary>Regions - Static reference to the region manager for accessing the region system.</summary>
        public static RegionManager Regions;
        /// <summary>Groups - Static reference to the group manager for accessing the group system.</summary>
        public static GroupManager Groups;
        /// <summary>Users - Static reference to the user manager for accessing the user database system.</summary>
        public static UserAccountManager UserAccounts;
        /// <summary>ProjectileBans - Static reference to the projectile ban system.</summary>
        public static ProjectileManagager ProjectileBans;
        /// <summary>TileBans - Static reference to the tile ban system.</summary>
        public static TileManager TileBans;
        /// <summary>RememberedPos - Static reference to the remembered position manager.</summary>
        public static RememberedPosManager RememberedPos;
        /// <summary>CharacterDB - Static reference to the SSC character manager.</summary>
        public static CharacterManager CharacterDB;
        /// <summary>Contains the information about what research has been performed in Journey mode.</summary>
        public static ResearchDatastore ResearchDatastore;
        #endregion

        #region Commons
        public static readonly TSPlayer[] Players = new TSPlayer[Main.maxPlayers + 1];
        #endregion

        #region Handlers
        internal static Bouncer Bouncer;
        public static ItemBans ItemBans;
        internal static RegionHandler RegionSystem;
        #endregion

        #region Misc
        /// <summary>
        /// Static reference to a <see cref="CommandLineParser"/> used for simple command-line parsing
        /// </summary>
        public static CommandLineParser CliParser { get; } = new CommandLineParser();
        /// <summary>
        /// only used for creating sample like item, projectile and npc (SetDefaults() requires it).
        /// </summary>
        internal static SampleServer ServerSample = new();
        internal static int SetupToken = -1;
        /// <summary>Geo - Static reference to the GeoIP system which determines the location of an IP address.</summary>
        public static GeoIPCountry Geo;
        /// <summary>
        /// Used for implementing REST Tokens prior to the REST system starting up.
        /// </summary>
        public static Dictionary<string, SecureRest.TokenData> RESTStartupTokens = [];
        public static SecureRest RestApi;
        /// <summary>RestManager - Static reference to the Rest API manager.</summary>
        public static RestManager RestManager;
        internal static ModuleManager ModuleManager = new();
        static TShock() {
            ServerSample.Main.player[Main.myPlayer] = new Player();
        }
        #endregion

#nullable enable

        #region Events
        public static event Action? DisposingEvent;
        #endregion

        public const int Order = 3;
        public override int InitializationOrder => Order;
        public override Task InitializeAsync(
            IPluginConfigRegistrar configRegistrar,
            ImmutableArray<PluginInitInfo> priorInitializations, 
            CancellationToken cancellationToken = default) {

            configRegistrar.DefaultOption
                .WithFormat(ConfigFormat.NewtonsoftJson)
                .OnSerializationFailure(SerializationFailureHandling.ThrowException)
                .OnDeserializationFailure(DeserializationFailureHandling.ThrowException);

            SavePath = Path.GetRelativePath(Directory.GetCurrentDirectory(), configRegistrar.Directory);
            Config = new TShockConfig(configRegistrar, "tshockconfig");
            ServerSideCharacterConfig = new ServerSideConfig(configRegistrar, "sscconfig", ServerSideConfig.Default);
            FileTools.SetupMiscFiles();

            Log ??= UnifierApi.CreateLogger(new LogHost());
            MiscInit();
            CliParser.Reset();
            HandleCommandLinePostConfigLoad(Environment.GetCommandLineArgs());

            DB = new DbBuilder(this, Config, configRegistrar.Directory).BuildDbConnection();

            Bans = new(DB);
            Warps = new(DB);
            Regions = new(DB);
            Groups = new(DB);
            UserAccounts = new(DB);
            ProjectileBans = new(DB);
            TileBans = new(DB);
            RememberedPos = new(DB);
            CharacterDB = new(DB);
            ResearchDatastore = new(DB);
            RestApi = new SecureRest(IPAddress.Any, Config.GlobalSettings.RestApiPort);
            RestManager = new RestManager(RestApi);
            RestManager.RegisterRestfulCommands();
            Bouncer = new Bouncer();
            RegionSystem = new RegionHandler(Regions);
            ItemBans = new ItemBans(this, DB);

            var geoippath = "GeoIP.dat";
            if (Config.GlobalSettings.EnableGeoIP && File.Exists(geoippath))
                Geo = new GeoIPCountry(geoippath);

            MiscHandler.Attach();
            Commands.InitCommands();
            GetDataHandlers.InitGetDataHandler();

            ModuleManager.Initialise([this]);

            if (Config.GlobalSettings.RestApiEnabled)
                RestApi.Start();

            return Task.CompletedTask;
        }
        class DebugLogFilter : ILogFilter
        {
            public bool ShouldLog(in LogEntry entry) => entry.Level > LogLevel.Debug || TShock.Config.GlobalSettings.DebugLogs;
        }
        static void MiscInit() {
            UnifierApi.LogCore.Filter = UnifierApi.LogCore.Filter! & new DebugLogFilter();
            ServerContext.RegisterExtension<TSServerData>(server => new());
            ServerContext.RegisterExtension(server => new TSServerPlayer(server));
            static void OnCreateSocket(ref ValueEventNoCancelArgs<CreateSocketEvent> args) {
                args.Content.Socket = new LinuxTcpSocket(args.Content.Client);
            }
            UnifierApi.EventHub.Coordinator.CreateSocket.Register(OnCreateSocket, HandlerPriority.Normal);
            On.OTAPI.HooksSystemContext.NetMessageSystemContext.InvokePlayerAnnounce += (orig, self, text, color, excludedPlr, plr, to, from) => {
                //TShock handles this
                return false;
            };
            Console.CancelKeyPress += (sender, args) => {
                args.Cancel = true;
            };
            Main.SettingsUnlock_WorldEvil = true;
        }
        public static void ApplyConfig() {
            var config = Config;

            foreach (var server in UnifiedServerCoordinator.Servers.Where(s => s.IsRunning)) {
                var settings = config.GetServerSettings(server.Name);

                server.NPC.defaultMaxSpawns = settings.DefaultMaximumSpawns;
                server.NPC.defaultSpawnRate = settings.DefaultSpawnRate;
            }

            Main.autoSave = config.GlobalSettings.AutoSave;

            if (config.GlobalSettings.MaxSlots > Main.maxPlayers - config.GlobalSettings.ReservedSlots)
                config.GlobalSettings.MaxSlots = Main.maxPlayers - config.GlobalSettings.ReservedSlots;

            Netplay.SpamCheck = false;
        }
        public static void HandleCommandLinePostConfigLoad(string[] parms) {
            var playerSet = new FlagSet("-maxplayers", "-players");
            var restTokenSet = new FlagSet("--rest-token", "-rest-token");
            var restEnableSet = new FlagSet("--rest-enabled", "-rest-enabled");
            var restPortSet = new FlagSet("--rest-port", "-rest-port");

            CliParser
                .AddFlags(restTokenSet, (token) =>
                {
                    RESTStartupTokens.Add(token, new SecureRest.TokenData { Username = "null", UserGroupName = "superadmin" });
                    Console.WriteLine(GetString("Startup parameter overrode REST token."));
                })
                .AddFlags(restEnableSet, (e) =>
                {
                    bool enabled;
                    if (bool.TryParse(e, out enabled)) {
                        Config.GlobalSettings.RestApiEnabled = enabled;
                        Console.WriteLine(GetString("Startup parameter overrode REST enable."));
                    }
                })
                .AddFlags(restPortSet, (p) => {
                    int restPort;
                    if (int.TryParse(p, out restPort)) {
                        Config.GlobalSettings.RestApiPort = restPort;
                        Console.WriteLine(GetString("Startup parameter overrode REST port."));
                    }
                })
                .AddFlags(playerSet, (p) =>
                {
                    int slots;
                    if (int.TryParse(p, out slots)) {
                        Config.GlobalSettings.MaxSlots = slots;
                        Console.WriteLine(GetString("Startup parameter overrode maximum player slot configuration value."));
                    }
                });

            CliParser.ParseFromSource(parms);
        }

        public override ValueTask DisposeAsync(bool isDisposing) {
            DisposingEvent?.Invoke();
            return base.DisposeAsync(isDisposing);
        }
    }
}
