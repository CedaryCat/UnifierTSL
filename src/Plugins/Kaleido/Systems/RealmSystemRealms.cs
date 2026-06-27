using System.Collections.Immutable;
using Kaleido.Model.Ids;
using Kaleido.Model.Instances;
using Kaleido.Model.Lifecycle;
using Kaleido.Model.Planning;
using Kaleido.Model.Transfer;
using Kaleido.Runtime;
using UnifierTSL.Servers;

namespace Kaleido.Systems
{
    public sealed class RealmSystemRealms(RealmOrchestrator orchestrator)
    {
        public ImmutableArray<RealmInstance> Instances => orchestrator.Registry.Instances;

        public bool TryGet(RealmKey key, out RealmInstance? instance) => orchestrator.Registry.TryGet(key, out instance);

        public bool TryGet(ServerContext server, out RealmInstance? instance) => orchestrator.Registry.TryGet(server, out instance);

        public Task<RealmInstance> EnsureAsync(RealmPlan plan, CancellationToken cancellationToken = default)
            => orchestrator.EnsureAsync(plan, cancellationToken);

        public Task<RealmPreparation> PrepareAsync(RealmPlan plan, RealmPrepareOptions? options = null, CancellationToken cancellationToken = default)
            => orchestrator.PrepareAsync(plan, options, cancellationToken);

        public RealmHold Hold(RealmInstance instance, TimeSpan? duration = null) => instance.Hold(duration);

        public Task<RealmTransferResult> TransferAsync(RealmTransferRequest request, CancellationToken cancellationToken = default)
            => orchestrator.TransferAsync(request, cancellationToken);

        public Task RetireAsync(RealmInstance instance, RealmRetireReason reason) => orchestrator.RetireAsync(instance, reason);
    }
}
