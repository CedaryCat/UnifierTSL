using Kaleido.Model.Instances;
using Kaleido.Model.Transfer;
using UnifierTSL.Servers;

namespace Kaleido.Systems.Installation
{
    public sealed record RealmPlayerEntering(int PlayerId, RealmInstance Instance, ServerContext Server, RealmEntry Entry);
    public sealed record RealmPlayerLeaving(int PlayerId, RealmInstance Instance, ServerContext Server, RealmExit Exit);

    public sealed class RealmTransferHooks(RealmInstallScope scope)
    {
        public IDisposable OnEntering(Action<RealmPlayerEntering> handler) {
            ArgumentNullException.ThrowIfNull(handler);
            return scope.RegisterEntering(handler);
        }

        public IDisposable OnLeaving(Action<RealmPlayerLeaving> handler) {
            ArgumentNullException.ThrowIfNull(handler);
            return scope.RegisterLeaving(handler);
        }
    }
}
