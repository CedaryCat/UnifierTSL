using Kaleido.Model.Hosting;
using Kaleido.Model.Ids;
using Kaleido.Model.Planning;
using Kaleido.Runtime;
using UnifierTSL.Events.Core;
using UnifierTSL.Events.Handlers;

namespace Kaleido.Hosting.SharedProjection
{
    public sealed class SharedProjectionHost : IRealmHost
    {
        private readonly ProjectionPacketRouter packetRouter;

        public SharedProjectionHost() {
            packetRouter = new();
            NetPacketHandler.ProcessPacketEvent.Register(packetRouter.OnPacket, HandlerPriority.Highest);
        }

        public RealmHostCapabilities Capabilities { get; } = new(
            RealmHostKind.SharedProjection,
            HasServerContext: false,
            HasProjection: true,
            HasRealEntities: false,
            SupportsMultiplePlayers: false,
            SupportsUnload: true);

        public bool CanHost(RealmPlan plan) => plan.Host.IsSatisfiedBy(Capabilities);

        public Task<RealmHostSession> StartAsync(RealmPlan plan, RealmOrchestrator orchestrator, RealmInstanceId instanceId, CancellationToken cancellationToken) {
            var server = plan.ContextFactory(new(orchestrator, plan, instanceId));
            if (server is not SharedProjectionContext context) {
                server.Dispose();
                throw new InvalidOperationException($"Realm '{plan.Key}' selected SharedProjection but its factory returned {server.GetType().FullName}.");
            }

            try {
                context.ProjectionDispatcher.Bind(orchestrator.Scheduler);
            }
            catch {
                context.Dispose();
                throw;
            }

            return Task.FromResult(new RealmHostSession(context, new SharedProjectionDriver(context)));
        }

        public void Dispose() {
            NetPacketHandler.ProcessPacketEvent.UnRegister(packetRouter.OnPacket);
        }
    }
}
