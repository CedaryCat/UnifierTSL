using Kaleido.Model.Hosting;
using Kaleido.Model.Ids;
using Kaleido.Model.Planning;
using Kaleido.Runtime;
using UnifierTSL.Servers;

namespace Kaleido.Hosting.ServerContext
{
    public sealed class ServerContextHost : IRealmHost
    {
        public RealmHostCapabilities Capabilities { get; } = new(
            RealmHostKind.ServerContext,
            HasServerContext: true,
            HasProjection: false,
            HasRealEntities: true,
            SupportsMultiplePlayers: true,
            SupportsUnload: true);

        public bool CanHost(RealmPlan plan) => plan.Host.IsSatisfiedBy(Capabilities);

        public async Task<RealmHostSession> StartAsync(RealmPlan plan, RealmOrchestrator orchestrator, RealmInstanceId instanceId, CancellationToken cancellationToken) {
            var session = Start(plan, orchestrator, instanceId);
            try {
                await session.Driver.WaitUntilReadyAsync(cancellationToken).ConfigureAwait(false);
            }
            catch {
                await session.Driver.StopAsync(new(Kaleido.Model.Lifecycle.RealmRetireKind.Failed, "Realm startup failed before the server became ready.")).ConfigureAwait(false);
                session.Driver.Dispose();
                throw;
            }

            return session;
        }

        private RealmHostSession Start(RealmPlan plan, RealmOrchestrator orchestrator, RealmInstanceId instanceId) {
            var server = plan.ContextFactory(new(orchestrator, plan, instanceId));
            if (server is SharedProjection.SharedProjectionContext) {
                server.Dispose();
                throw new InvalidOperationException($"Realm '{plan.Key}' selected ServerContext but returned a shared projection context.");
            }

            server.Run([]);
            ServerRuntime.Register(server);
            return new(server, new ServerContextHandle(server));
        }

        public void Dispose() { }
    }
}
