using UnifierTSL.Logging;

namespace UnifierTSL
{
    public partial class UnifierApi
    {
        public static RoleLogger CreateLogger(ILoggerHost host, Logger? overrideLogger = null) {
            return new RoleLogger(host, overrideLogger ?? LogCore);
        }

        public static void UpdateTitle(bool empty = false) {
            Console.Title = $"UnifierTSL " +
                            $"- {(empty ? 0 : UnifiedServerCoordinator.GetActiveClientCount())}/{byte.MaxValue} " +
                            $"@ {UnifiedServerCoordinator.ListeningEndpoint} " +
                            $"USP for Terraria v{VersionHelper.TerrariaVersion}";
        }
    }
}
