using Kaleido.Model.Ids;
using Kaleido.Model.Lifecycle;
using Kaleido.Model.Transfer;

namespace Kaleido.Hosting
{
    public interface IRealmDriver : IDisposable
    {
        RealmRuntimeState State { get; }
        Task WaitUntilReadyAsync(CancellationToken cancellationToken);
        void Attach(RealmPlayer player, RealmEntry entry);
        void Detach(RealmPlayer player, RealmExit exit);
        void Tick(RealmTick tick);
        Task StopAsync(RealmRetireReason reason);
    }
}
