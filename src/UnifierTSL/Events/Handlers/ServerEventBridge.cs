using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnifiedServerProcess;
using UnifierTSL.Events.Core;
using UnifierTSL.Servers;

namespace UnifierTSL.Events.Handlers
{
    public struct CreateConsoleServiceEvent(ServerContext server) : IEventContent
    {
        public readonly ServerContext Server = server;
        public ConsoleSystemContext? Console;
    }
    public class ServerEventBridge
    {
        public readonly ValueEventNoCancelProvider<CreateConsoleServiceEvent> CreateConsoleServiceEvent = new();
        public ConsoleSystemContext? CreateConsoleService(ServerContext server) {
            var args = new CreateConsoleServiceEvent(server);
            CreateConsoleServiceEvent.Invoke(ref args);
            return args.Console;
        }
    }
}
