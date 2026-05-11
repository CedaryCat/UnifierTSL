using System.Collections.Immutable;
using UnifierTSL;
using UnifierTSL.Logging;
using UnifierTSL.Performance;
using UnifierTSL.Servers;

namespace Atelier.Session.Context
{
    public sealed class ServerGlobals(ServerContext context)
    {
        public ServerContext Context { get; } = context ?? throw new ArgumentNullException(nameof(context));

        public ServerDispatcher Dispatcher => Context.Dispatcher;

        public RoleLogger Log => Context.Log;

        public string Name => Context.Name;

        public Guid UniqueId => Context.UniqueId;

        public bool IsRunning => Context.IsRunning;

        public Thread? RunningThread => Context.RunningThread;

        public int ActivePlayerCount => Context.ActivePlayerCount;

        public ImmutableArray<ServerContext> Peers => [.. UnifiedServerCoordinator.Servers.Where(server => !ReferenceEquals(server, Context))];

        public PerformanceSnapshot Snapshot(TimeSpan window) {
            return ServerPerformance.Queries.GetSnapshot(Context, window);
        }
    }
}
