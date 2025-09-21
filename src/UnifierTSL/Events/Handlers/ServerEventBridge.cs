using UnifiedServerProcess;
using UnifierTSL.Events.Core;
using UnifierTSL.Servers;

namespace UnifierTSL.Events.Handlers
{
    public struct CreateConsoleServiceEvent(ServerContext server) : IServerEventContent
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
    public class ServerEventBridge
    {
        public readonly ValueEventNoCancelProvider<CreateConsoleServiceEvent> CreateConsoleService = new();
        public readonly ReadonlyEventNoCancelProvider<AddServer> AddServer = new();
        public readonly ReadonlyEventNoCancelProvider<RemoveServer> RemoveServer = new();
        public readonly ReadonlyEventNoCancelProvider<ServerListChanged> ServerListChanged = new();
        public ConsoleSystemContext? InvokeCreateConsoleService(ServerContext server) {
            CreateConsoleServiceEvent args = new(server);
            CreateConsoleService.Invoke(ref args);
            return args.Console;
        }
    }
}
