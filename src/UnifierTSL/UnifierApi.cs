using UnifierTSL.PluginService.Loading;
using UnifierTSL.Servers;
using static Terraria.GameContent.UI.States.UIWorldCreation;

namespace UnifierTSL
{
    public class UnifierApi
    {
        public static Version TerrariaVersion { get; } = new(1, 4, 4, 9);
        public static readonly EventHub EventHub = new();
        public static PluginsLoadContext PluginsContext { get; internal set; } = null!;
        public static int ListenPort { get; internal set; } = -1;
        private static string? serverPassword;
        public static string ServerPassword => serverPassword ?? "";
        internal static void Initialize(string[] parms) {

            PluginsContext = new PluginLoader(new DirectoryInfo(Directory.GetCurrentDirectory()))
                .LoadPluginInfo(out var vaildPluginInfos, out var nameConflicts)
                .HandleDependencies(out var validPlugins, out var failedPlugins)
                .Initialize();

            HandleCommandLine(parms);
            ReadLauncherArgs();
        }
        static void HandleCommandLine(string[] parms) {
            Dictionary<string, List<string>> args = Utilities.CLI.ParseArguements(parms);
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
