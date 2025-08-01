using Steamworks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnifierTSL.Logging;

namespace UnifierTSL
{
    public partial class UnifierApi
    {
        public static RoleLogger CreateLogger(ILoggerHost host, Logger? overrideLogger = null) {
            return new RoleLogger(host, overrideLogger ?? LogCore);
        }

        public static void UpdateTitle() {
            Console.Title = $"UnifierTSL " +
                            $"- {UnifiedServerCoordinator.GetActiveClientCount()}/{byte.MaxValue} " +
                            $"@ {UnifiedServerCoordinator.ListeningEndpoint} " +
                            $"USP for Terraria v{VersionHelper.TerrariaVersion}";
        }
    }
}
