using Terraria.Localization;
using UnifierTSL.PluginHost;

namespace UnifierTSL
{
    internal class Program
    {
        private static void Main(string[] args) {
            Initializer.InitializeResolver();
            UnifierApi.HandleCommandLinePreRun(args);
            Run(args);
        }

        private static void Run(string[] args) {
            VersionHelper version = UnifierApi.VersionHelper;

            Console.Title = "UnifierTSLauncher";

            Console.WriteLine(@" ╔════════════════════════════════════════════════════════════════════════════════════╗");
            Console.WriteLine(@"╔═══════════════════════════════════════════════════════════════════════════════════╗╗║");
            Console.WriteLine(@"║   ██╗   ██╗███╗   ██╗██╗███████╗██╗███████╗██████╗     ████████╗███████╗██╗       ║║║");
            Console.WriteLine(@"║   ██║   ██║████╗  ██║██║██╔════╝██║██╔════╝██╔══██╗    ╚══██╔══╝██╔════╝██║       ║║║");
            Console.WriteLine(@"║   ██║   ██║██╔██╗ ██║██║█████╗  ██║█████╗  ██████╔╝       ██║   ███████╗██║       ║ ║");
            Console.WriteLine(@"║   ██║   ██║██║╚██╗██║██║██╔══╝  ██║██╔══╝  ██╔══██╗       ██║   ╚════██║██║       ║ ║");
            Console.WriteLine(@"║   ╚██████╔╝██║ ╚████║██║██║     ██║███████╗██║  ██║       ██║   ███████║███████╗  ║║║");
            Console.WriteLine(@"║    ╚═════╝ ╚═╝  ╚═══╝╚═╝╚═╝     ╚═╝╚══════╝╚═╝  ╚═╝       ╚═╝   ╚══════╝╚══════╝  ║║╝");
            Console.WriteLine(@"╚═══════════════════════════════════════════════════════════════════════════════════╝");

            Console.WriteLine();

            UnifierApi.Logger.Info(GetString(
@$"Unifier Terraria-Server-Launcher Running
Version Info:
  Terraria v{version.TerrariaVersion} & OTAPI v{version.OTAPIVersion}
  Unified-Server-Process v{version.USPVersion}
  UnifierApi v{version.UnifierApiVersion} & PluginApi v{PluginOrchestrator.ApiVersion}
Current Process ID: {Environment.ProcessId}"));

            WorkRunner.RunTimedWork("Init", GetString("Global initialization started..."), () => {
                Initializer.Initialize();
                UnifierApi.InitializeCore(args);
            });

            UnifierApi.CompleteLauncherInitialization();

            UnifiedServerCoordinator.Launch(UnifierApi.ListenPort, UnifierApi.ServerPassword);

            UnifierApi.UpdateTitle();

            string currentServers = "";
            if (UnifiedServerCoordinator.Servers.Length > 0) {
                currentServers = GetString("Current Servers: ") + "\r\n";
                foreach (Servers.ServerContext server in UnifiedServerCoordinator.Servers) {
                    currentServers += GetParticularString("{0} is server name, {1} is world file name", $"  {server.Name} Running on world: {server.worldDataProvider.WorldFileName}") + "\r\n";
                }
            }

            UnifiedServerCoordinator.Logger.Info(
                category: "Startup",
                message: GetString($"UnifierTSL started successfully!") + "\r\n" +
                         currentServers +
                         Language.GetTextValue("CLI.ListeningOnPort", UnifiedServerCoordinator.ListenPort) + "\r\n" +
                         (string.IsNullOrEmpty(UnifiedServerCoordinator.ServerPassword)
                         ? GetString($"Server is running without a password. Anyone can join.")
                         : GetParticularString("{0} is server password", $"Server is running with password: '{UnifiedServerCoordinator.ServerPassword}'")));

            UnifierApi.EventHub.Coordinator.Started.Invoke(default);
            UnifierApi.EventHub.Chat.KeepReadingInput();
        }
    }
}
