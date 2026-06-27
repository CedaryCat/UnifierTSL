using Kaleido.Model.Ids;
using Kaleido.Runtime;

namespace Kaleido.Model.Planning
{
    public sealed record RealmCreation(RealmOrchestrator Orchestrator, RealmPlan Plan, RealmInstanceId InstanceId);
}
