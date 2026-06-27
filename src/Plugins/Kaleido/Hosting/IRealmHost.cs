using Kaleido.Model.Hosting;
using Kaleido.Model.Ids;
using Kaleido.Model.Planning;
using Kaleido.Runtime;

namespace Kaleido.Hosting
{
    public interface IRealmHost : IDisposable
    {
        RealmHostCapabilities Capabilities { get; }
        bool CanHost(RealmPlan plan);
        Task<RealmHostSession> StartAsync(RealmPlan plan, RealmOrchestrator orchestrator, RealmInstanceId instanceId, CancellationToken cancellationToken);
    }
}
