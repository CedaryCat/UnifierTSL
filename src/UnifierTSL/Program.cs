using Terraria.Localization;
using UnifierTSL.Events.Handlers;

namespace UnifierTSL
{
    internal class Program
    {
        static void Main(string[] args) {
            Initializer.InitializeResolver();
            Run(args);
        }

        static void Run(string[] args) {
            var version = new VersionHelper();

            Console.Title = "UnifierTSLauncher";

            Console.WriteLine(@"___________________________________________________________________________________________________");
            Console.WriteLine(@"_  _ _  _ _ ____ _ ____ ___     ____ ____ ____ _  _ ____ ____    ___  ____ ____ ____ ____ ____ ____");
            Console.WriteLine(@"|  | |\ | | |___ | |___ |  \    [__  |___ |__/ |  | |___ |__/    |__] |__/ |  | |    |___ [__  [__ ");
            Console.WriteLine(@"|__| | \| | |    | |___ |__/    ___] |___ |  \  \/  |___ |  \    |    |  \ |__| |___ |___ ___] ___]");
            Console.WriteLine(@"---------------------------------------------------------------------------------------------------");
            Console.WriteLine(@"                       Demonstration For Terraria v{0} & OTAPI v{1}                         ", version.TerrariaVersion, version.OTAPIVersion);
            Console.WriteLine(@"---------------------------------------------------------------------------------------------------");

            WorkRunner.RunTimedWork("Init", "Global initialization started...", () => {
                Initializer.Initialize();
                UnifierApi.Initialize(args);
            });

            UnifiedServerCoordinator.Launch(UnifierApi.ListenPort, UnifierApi.ServerPassword);

            Console.Title = $"UnifierTSL " +
                            $"- {UnifiedServerCoordinator.GetActiveClientCount()}/{byte.MaxValue} " +
                            $"@ {UnifiedServerCoordinator.ListeningEndpoint} " +
                            $"USP for Terraria v{version.TerrariaVersion}";

            UnifiedServerCoordinator.Logger.Info(
                category: "Startup",
                message: "UnifierTSLauncher started successfully! \r\n" +
                         Language.GetTextValue("CLI.ListeningOnPort", UnifiedServerCoordinator.ListenPort));

            while (true) {
                Console.ReadLine();
            }
        }
    }
}
