using System.Collections.ObjectModel;
using System.Globalization;
using Terraria.Localization;
using Terraria.Utilities;
using UnifierTSL.Events.Core;
using UnifierTSL.Events.Handlers;
using UnifierTSL.Extensions;
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
        public static EventHub EventHub { get; private set; } = null!;
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
        private class LoggerHost : ILoggerHost
        {
            public string Name => "UnifierApi";
            public string? CurrentLogCategory { get; set; }
        }
        private static readonly ILoggerHost LogHost = new LoggerHost();
        private static readonly RoleLogger logger;
        public static RoleLogger Logger => logger;

        private static Logger logCore;
        public static Logger LogCore {
            get => logCore ??= new Logger();
        }
        #endregion 

        static UnifierApi() {
            logCore = new();
            logger = CreateLogger(LogHost);
        }

        internal static void Initialize(string[] launcherArgs) {
            EventHub = new();

            pluginHosts = new();
            PluginHosts.InitializeAllAsync()
                .GetAwaiter()
                .GetResult();

            HandleCommandLine(launcherArgs);
            ReadLauncherArgs();
            EventHub.Lanucher.InitializedEvent.Invoke(new());
        }
        internal static void HandleCommandLinePreRun(string[] launcherArgs) {

            bool langSetted = false;

            if (Environment.GetEnvironmentVariable("UTSL_LANGUAGE") is string overrideLang) {
                langSetted = TrySetLang(overrideLang);
            }

            Dictionary<string, List<string>> args = Utilities.CLI.ParseArguements(launcherArgs);
            foreach (KeyValuePair<string, List<string>> arg in args) {
                string firstValue = arg.Value[0];
                switch (arg.Key) {
                    case "-culture":
                    case "-lang":
                    case "-language": {
                            if (langSetted) {
                                break;
                            }
                            if (TrySetLang(firstValue)) {
                                langSetted = true;
                            }
                        }
                        break;
                }
            }

            static bool TrySetLang(string langArg) {
                if (int.TryParse(langArg, out int langId) && GameCulture._legacyCultures.TryGetValue(langId, out var culture)) {
                    SetLang(culture);
                    return true;
                }
                culture = GameCulture._legacyCultures.Values.SingleOrDefault(c => c.Name == langArg);
                if (culture is not null) {
                    SetLang(culture);
                    return true;
                }
                return false;

                static void SetLang(GameCulture culture) {
                    GameCulture.DefaultCulture = culture;
                    LanguageManager.Instance.SetLanguage(culture);
                    CultureInfo.CurrentCulture = culture.RedirectedCultureInfo();
                }
            }
        }
        private static void HandleCommandLine(string[] launcherArgs) {
            Dictionary<string, List<string>> args = Utilities.CLI.ParseArguements(launcherArgs);
            bool settedJoinServer = false;
            foreach (KeyValuePair<string, List<string>> arg in args) {
                string firstValue = arg.Value[0];
                switch (arg.Key) {
                    case "-listen":
                    case "-port":
                        if (int.TryParse(firstValue, out int port)) {
                            ListenPort = port;
                        }
                        else {
                            Console.WriteLine(GetParticularString("{0} is user input value for port number", $"Invalid port number specified: {firstValue}"));
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

        private static void AutoStartServer(KeyValuePair<string, List<string>> arg) {
            foreach (string serverArgs in arg.Value) {
                string? serverName = null;
                string? worldName = null;
                string seed = "";
                int difficulty = 2;
                int size = 3;
                int evil = 0;

                if (!Utilities.CLI.TryParseSubArguements(serverArgs, out Dictionary<string, string>? result)) {
                    Console.WriteLine(GetParticularString("{0} is server argument string", $"Invalid server argument: {serverArgs}"));
                    Console.WriteLine(GetString($"Expected format: -server name:<name> worldname:<worldname> gamemode:<value> size:<value> evil:<value> seed:<value>"));
                    goto failToAddServer;
                }

                foreach (KeyValuePair<string, string> serverArg in result) {
                    switch (serverArg.Key) {
                        case "name": {
                                serverName = serverArg.Value.Trim();
                                if (UnifiedServerCoordinator.Servers.Any(s => s.Name == serverName)) {
                                    Console.WriteLine(GetParticularString("{0} is server name", $"Server name '{serverName}' is already in use"));
                                    goto failToAddServer;
                                }
                                break;
                            }
                        case "worldname": {
                                worldName = serverArg.Value.Trim();
                                ServerContext? nameConflict = UnifiedServerCoordinator.Servers.FirstOrDefault(s => s.Main.worldName == worldName);
                                if (nameConflict is not null) {
                                    Console.WriteLine(GetParticularString("{0} is world name, {1} is server name", $"World name '{worldName}' is already in use by server '{nameConflict.Name}'"));
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
                                string serverArgVal = serverArg.Value.Trim();
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
                                        Console.WriteLine(GetParticularString("{0} is user input value for server difficulty", $"Invalid server difficulty: {serverArgVal}"));
                                        Console.WriteLine(GetString($"Expected value: an integer between 0 and 3 (inclusive), or one of the strings: normal, expert, master, creative"));
                                        goto failToAddServer;
                                    }
                                }
                                break;
                            }
                        case "size": {
                                string serverArgVal = serverArg.Value.Trim();
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
                                        Console.WriteLine(GetParticularString("{0} is user input value for server size", $"Invalid server size: {serverArgVal}"));
                                        Console.WriteLine(GetString($"Expected value: an integer between 0 and 2 (inclusive), or one of the strings: small, medium, large"));
                                        goto failToAddServer;
                                    }
                                }
                                break;
                            }
                        case "evil": {
                                string serverArgVal = serverArg.Value.Trim().ToLower();
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
                                        Console.WriteLine(GetParticularString("{0} is user input value for server evil (world corruption/crimson type)", $"Invalid server evil: {serverArg.Value}"));
                                        Console.WriteLine(GetString($"Expected value: an integer between 0 and 2 (inclusive), or one of the strings: random, corruption, crimson"));
                                        goto failToAddServer;
                                    }
                                }
                            }
                            break;
                    }
                }

                if (string.IsNullOrWhiteSpace(worldName)) {
                    Console.WriteLine(GetString("Parameter 'worldname' is required."));
                    goto failToAddServer;
                }

                if (string.IsNullOrWhiteSpace(serverName)) {
                    Console.WriteLine(GetString("Parameter 'name' is required."));
                    goto failToAddServer;
                }

                ServerContext server = new(serverName, IWorldDataProvider.GenerateOrLoadExisting(worldName, size, difficulty, evil, seed));
                Task.Run(() => server.Program.LaunchGame([]));
                UnifiedServerCoordinator.AddServer(server);

                continue;
            failToAddServer:
                continue;
            }
        }

        private static void ReadLauncherArgs() {
            while (ListenPort < 0 || ListenPort >= ushort.MaxValue) {
                Console.Write(GetString($"Enter the port to listen on: "));
                if (int.TryParse(Console.ReadLine(), out int port)) {
                    ListenPort = port;
                }
            }
            if (serverPassword is null) {
                Console.Write(GetString($"Enter the server password: "));
                serverPassword = Console.ReadLine()?.Trim() ?? "";
            }
        }
    }
}
