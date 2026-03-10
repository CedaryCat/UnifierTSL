using UnifiedServerProcess;
using UnifierTSL.Events.Core;
using UnifierTSL.Servers;

namespace UnifierTSL.Events.Handlers
{
    public struct CreateServerConsoleServiceEvent(ServerContext server) : IServerEventContent
    {
        public ServerContext Server { get; init; } = server;
        public ConsoleSystemContext? Console;
    }
    public readonly struct AddServer(ServerContext server) : IServerEventContent
    {
        public ServerContext Server { get; init; } = server;
    }
    public readonly struct RemoveServer(ServerContext server) : IServerEventContent
    {
        public ServerContext Server { get; init; } = server;
    }
    public readonly struct ServerListChanged() : IEventContent
    {
    }
    public class ServerConsoleEventBridge
    {
        public readonly ValueEventNoCancelProvider<CreateServerConsoleServiceEvent> CreateServerConsoleService = new();
        public readonly ReadonlyEventNoCancelProvider<AddServer> AddServer = new();
        public readonly ReadonlyEventNoCancelProvider<RemoveServer> RemoveServer = new();
        public readonly ReadonlyEventNoCancelProvider<ServerListChanged> ServerListChanged = new();
        public ConsoleSystemContext? InvokeCreateServerConsoleService(ServerContext server) {
            CreateServerConsoleServiceEvent args = new(server);
            CreateServerConsoleService.Invoke(ref args);
            return args.Console;
        }
    }
}
