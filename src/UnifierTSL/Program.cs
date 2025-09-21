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

            UnifierApi.Logger.Info($"Unifier Terraria-Server-Launcher Running\r\n" +
                $"Version Info: \r\n" +
                $"  Terraria v{version.TerrariaVersion} & OTAPI v{version.OTAPIVersion}\r\n" +
                $"  Unified-Server-Process v{version.USPVersion}\r\n" +
                $"  UnifierApi v{version.UnifierApiVersion} & PluginApi v{PluginOrchestrator.ApiVersion}\r\n" +
                $"Current Process ID: {Environment.ProcessId}");

            WorkRunner.RunTimedWork("Init", "Global initialization started...", () => {
                Initializer.Initialize();
                UnifierApi.Initialize(args);
            });

            UnifiedServerCoordinator.Launch(UnifierApi.ListenPort, UnifierApi.ServerPassword);

            UnifierApi.UpdateTitle();

            string currentServers = "";
            if (UnifiedServerCoordinator.Servers.Length > 0) {
                currentServers = "Current Servers: \r\n";
                foreach (Servers.ServerContext server in UnifiedServerCoordinator.Servers) {
                    currentServers += $"  {server.Name} Running on world: {server.worldDataProvider.WorldFileName}\r\n";
                }
            }

            UnifiedServerCoordinator.Logger.Info(
                category: "Startup",
                message: "UnifierTSL started successfully! \r\n" +
                         currentServers +
                         Language.GetTextValue("CLI.ListeningOnPort", UnifiedServerCoordinator.ListenPort) + "\r\n" +
                         (string.IsNullOrEmpty(UnifiedServerCoordinator.ServerPassword)
                         ? "Server is running without a password. -anyone can join."
                         : $"Server is running with password: '{UnifiedServerCoordinator.ServerPassword}'"));

            UnifierApi.EventHub.Coordinator.Started.Invoke(default);
            UnifierApi.EventHub.Chat.KeepReadingInput();
        }
    }
}
