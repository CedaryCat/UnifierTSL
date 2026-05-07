using UnifierTSL.Events.Core;
using UnifierTSL.Servers;

namespace UnifierTSL.Events.Handlers
{
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
        public readonly ReadonlyEventNoCancelProvider<AddServer> AddServer = new();
        public readonly ReadonlyEventNoCancelProvider<RemoveServer> RemoveServer = new();
        public readonly ReadonlyEventNoCancelProvider<ServerListChanged> ServerListChanged = new();
    }
}
