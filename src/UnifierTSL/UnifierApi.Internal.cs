using Terraria.Localization;
using Terraria.Utilities;
using UnifierTSL.Events.Core;
using UnifierTSL.Events.Handlers;
using UnifierTSL.FileSystem;
using UnifierTSL.Logging;
using UnifierTSL.PluginHost;
using UnifierTSL.Servers;
using static Terraria.GameContent.UI.States.UIWorldCreation;

namespace UnifierTSL
{
    public static partial class UnifierApi
    {
        private static VersionHelper? version;
        public static VersionHelper VersionHelper => version ??= new();

        public static readonly UnifiedRandom rand = new();

        #region Events
        public static readonly EventHub EventHub = new();
        #endregion

        #region Plugins
        public static PluginOrchestrator? pluginHosts;
        public static PluginOrchestrator PluginHosts => pluginHosts ??= new();
        #endregion

        #region Parameters
        internal static int ListenPort { get; set; } = -1;
        private static string? serverPassword;
        internal static string ServerPassword => serverPassword ?? "";
        #endregion

        #region IO
        internal static readonly DirectoryWatchManager FileMonitor = new(Directory.GetCurrentDirectory());
        #endregion

        #region Logging
        class LoggerHost : ILoggerHost
        {
            public string Name => "UnifierApi";
            public string? CurrentLogCategory { get; set; }
        }
        private readonly static ILoggerHost LogHost = new LoggerHost();
        private readonly static RoleLogger logger;
        public static RoleLogger Logger => logger;

        static Logger logCore;
        public static Logger LogCore {
            get => logCore ??= new Logger();
        }
        #endregion 

        static UnifierApi() {
            logCore = new();
            logger = CreateLogger(LogHost);
        }

        internal static void Initialize(string[] launcherArgs) {
            pluginHosts = new();
            PluginHosts.InitializeAllAsync()
                .GetAwaiter()
                .GetResult();

            HandleCommandLine(launcherArgs);
            ReadLauncherArgs();
        }

        internal static void HandleCommandLinePreRun(string[] launcherArgs) {
            Dictionary<string, List<string>> args = Utilities.CLI.ParseArguements(launcherArgs);
            foreach (var arg in args) {
                var firstValue = arg.Value[0];
                switch (arg.Key) {
                    case "-culture":
                    case "-lang":
                    case "-language": {
                            if (int.TryParse(firstValue, out var langId) && GameCulture._legacyCultures.TryGetValue(langId, out var culture)) {
                                GameCulture.DefaultCulture = culture;
                            }
                        }
                        break;
                }
            }
        }
        static void HandleCommandLine(string[] launcherArgs) {
            Dictionary<string, List<string>> args = Utilities.CLI.ParseArguements(launcherArgs);
            bool settedJoinServer = false;
            foreach (var arg in args) {
                var firstValue = arg.Value[0];
                switch (arg.Key) { 
                    case "-listen":
                    case "-port":
                        if (int.TryParse(firstValue, out int port)) {
                            ListenPort = port;
                        }
                        else {
                            Console.WriteLine("Invalid port number specified: {0}", arg.Value);
                        }
                        break;
                    case "-password":
                        serverPassword = firstValue;
                        break;
                    case "-autostart":
                    case "-addserver":
                    case "-server":
                        AutoStartServer(arg);
                        break;
                    case "-joinserver":
                        if (settedJoinServer) {
                            break;
                        }
                        static void JoinRandom(ref ValueEventNoCancelArgs<SwitchJoinServerEvent> arg) {
                            if (arg.Content.Servers.Length > 0) {
                                arg.Content.JoinServer = arg.Content.Servers[rand.Next(0, arg.Content.Servers.Length)];
                            }
                        }
                        static void JoinFirst(ref ValueEventNoCancelArgs<SwitchJoinServerEvent> arg) {
                            if (arg.Content.Servers.Length > 0) {
                                arg.Content.JoinServer = arg.Content.Servers[0];
                            }
                        }
                        if (firstValue is "random" or "rnd" or "r") {
                            settedJoinServer = true;
                            EventHub.Coordinator.SwitchJoinServer.Register(JoinRandom, HandlerPriority.Lowest);
                        }
                        else if (firstValue is "first" or "f") {
                            settedJoinServer = true;
                            EventHub.Coordinator.SwitchJoinServer.Register(JoinFirst, HandlerPriority.Lowest);
                        }
                        break;
                }
            }
        }

        static void AutoStartServer(KeyValuePair<string, List<string>> arg) {
            foreach (var serverArgs in arg.Value) {
                string? serverName = null;
                string? worldName = null;
                string seed = "";
                int difficulty = 2;
                int size = 3;
                int evil = 0;

                if (!Utilities.CLI.TryParseSubArguements(serverArgs, out var result)) {
                    Console.WriteLine("Invalid server argument: {0}", arg.Value);
                    Console.WriteLine("Expected format: -server name:<name> worldname:<worldname> gamemode:<value> size:<value> evil:<value> seed:<value>");
                    goto failToAddServer;
                }

                foreach (var serverArg in result) {
                    switch (serverArg.Key) {
                        case "name": {
                                serverName = serverArg.Value.Trim();
                                if (UnifiedServerCoordinator.Servers.Any(s => s.Name == serverName)) {
                                    Console.WriteLine("Server name '{0}' is already in used", serverName);
                                    goto failToAddServer;
                                }
                                break;
                            }
                        case "worldname": {
                                worldName = serverArg.Value.Trim();
                                var nameConflict = UnifiedServerCoordinator.Servers.FirstOrDefault(s => s.Main.worldName == worldName);
                                if (nameConflict is not null) {
                                    Console.WriteLine("World name '{0}' is already in used by server '{1}'", worldName, nameConflict.Name);
                                    goto failToAddServer;
                                }
                                break;
                            }
                        case "seed": {
                                seed = serverArg.Value.Trim();
                                break;
                            }
                        case "gamemode":
                        case "difficulty": {
                                var serverArgVal = serverArg.Value.Trim();
                                if (!int.TryParse(serverArgVal, out difficulty) || difficulty < 0 || difficulty > 3) {
                                    if (serverArgVal.Equals(nameof(WorldDifficultyId.Normal), StringComparison.OrdinalIgnoreCase) || serverArgVal == "n") {
                                        difficulty = 0;
                                    }
                                    else if (serverArgVal.Equals(nameof(WorldDifficultyId.Expert), StringComparison.OrdinalIgnoreCase) || serverArgVal == "e") {
                                        difficulty = 1;
                                    }
                                    else if (serverArgVal.Equals(nameof(WorldDifficultyId.Master), StringComparison.OrdinalIgnoreCase) || serverArgVal == "m") {
                                        difficulty = 2;
                                    }
                                    else if (serverArgVal.Equals(nameof(WorldDifficultyId.Creative), StringComparison.OrdinalIgnoreCase) || serverArgVal == "c") {
                                        difficulty = 3;
                                    }
                                    else {
                                        Console.WriteLine("Invalid server difficulty: {0}", serverArgVal);
                                        Console.WriteLine("Expected value: an integer between 0 and 3 (inclusive), or one of the strings: normal, expert, master, creative");
                                        goto failToAddServer;
                                    }
                                }
                                break;
                            }
                        case "size": {
                                var serverArgVal = serverArg.Value.Trim();
                                if (!int.TryParse(serverArgVal, out size) || size < 1 || size > 3) {
                                    if (serverArgVal.Equals(nameof(WorldSizeId.Small), StringComparison.OrdinalIgnoreCase) || serverArgVal == "s") {
                                        size = 1;
                                    }
                                    else if (serverArgVal.Equals(nameof(WorldSizeId.Medium), StringComparison.OrdinalIgnoreCase) || serverArgVal == "m") {
                                        size = 2;
                                    }
                                    else if (serverArgVal.Equals(nameof(WorldSizeId.Large), StringComparison.OrdinalIgnoreCase) || serverArgVal == "l") {
                                        size = 3;
                                    }
                                    else {
                                        Console.WriteLine("Invalid server size: {0}", serverArgVal);
                                        Console.WriteLine("Expected value: an integer between 0 and 2 (inclusive), or one of the strings: small, medium, large");
                                        goto failToAddServer;
                                    }
                                }
                                break;
                            }
                        case "evil": {
                                var serverArgVal = serverArg.Value.Trim().ToLower();
                                if (!int.TryParse(serverArg.Value, out evil) || (evil < 0 && evil > 2)) {
                                    if (serverArgVal.Equals(nameof(WorldEvilId.Random), StringComparison.OrdinalIgnoreCase)) {
                                        evil = 0;
                                    }
                                    else if (serverArgVal.Equals(nameof(WorldEvilId.Corruption), StringComparison.OrdinalIgnoreCase)) {
                                        evil = 1;
                                    }
                                    else if (serverArgVal.Equals(nameof(WorldEvilId.Crimson), StringComparison.OrdinalIgnoreCase)) {
                                        evil = 2;
                                    }
                                    else {
                                        Console.WriteLine("Invalid server evil: {0}", serverArg.Value);
                                        Console.WriteLine("Expected value: an integer between 0 and 2 (inclusive), or one of the strings: random, corruption, crimson");
                                        goto failToAddServer;
                                    }
                                }
                            }
                            break;
                    }
                }

                if (string.IsNullOrWhiteSpace(worldName)) {
                    Console.WriteLine("Parameter 'worldname' is required.");
                    goto failToAddServer;
                }

                if (string.IsNullOrWhiteSpace(serverName)) {
                    Console.WriteLine("Parameter 'name' is required.");
                    goto failToAddServer;
                }

                var server = new ServerContext(serverName, IWorldDataProvider.GenerateOrLoadExisting(worldName, size, difficulty, evil, seed));
                Task.Run(() => server.Program.LaunchGame([]));
                UnifiedServerCoordinator.AddServer(server);

                continue;
            failToAddServer:
                continue;
            }
        }

        static void ReadLauncherArgs() {
            while (ListenPort < 0 || ListenPort >= ushort.MaxValue) {
                Console.Write("Enter the port to listen on: ");
                if (int.TryParse(Console.ReadLine(), out int port)) {
                    ListenPort = port;
                }
            }
            if (serverPassword is null) {
                Console.WriteLine("Enter the server password: ");
                serverPassword = Console.ReadLine()?.Trim() ?? "";
            }
        }
    }
}
