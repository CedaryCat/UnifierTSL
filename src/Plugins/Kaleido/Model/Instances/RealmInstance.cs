using System.Collections.Immutable;
using Kaleido.Model.Ids;
using Kaleido.Model.Lifecycle;
using Kaleido.Model.Planning;
using Kaleido.Model.Transfer;
using Kaleido.Runtime;
using UnifierTSL.Servers;

namespace Kaleido.Model.Instances
{
    public sealed class RealmInstance
    {
        private readonly RealmRuntime runtime;

        internal RealmInstance(RealmRuntime runtime) {
            this.runtime = runtime;
        }

        internal RealmRuntime Runtime => runtime;
        public RealmInstanceId InstanceId => runtime.InstanceId;
        public RealmPlan Plan => runtime.Plan;
        public ServerContext Server => runtime.Server;
        public DateTimeOffset CreatedAtUtc => runtime.CreatedAtUtc;
        public RealmRuntimeState State => runtime.State;
        public int PlayerCount => runtime.PlayerCount;
        public ImmutableArray<int> Players => runtime.Players;

        public RealmHold Hold(TimeSpan? duration = null) => runtime.Hold(duration);

        public Task RetireAsync(RealmRetireReason reason) => runtime.Orchestrator.RetireAsync(this, reason);

        public Task<RealmTransferResult> TransferPlayerAsync(
            int playerId,
            RealmEntry? entry = null,
            RealmExit? exit = null,
            ServerTransferOptions? options = null,
            CancellationToken cancellationToken = default) {

            return runtime.Orchestrator.TransferAsync(
                RealmTransferRequest.ToServer(playerId, Server, entry, exit, options),
                cancellationToken);
        }
    }
}
