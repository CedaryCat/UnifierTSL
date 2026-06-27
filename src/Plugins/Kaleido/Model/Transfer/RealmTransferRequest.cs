using Kaleido.Model.Planning;
using UnifierTSL.Servers;

namespace Kaleido.Model.Transfer
{
    public sealed record RealmTransferRequest(
        int PlayerId,
        RealmTarget Target,
        RealmEntry Entry,
        RealmExit Exit,
        ServerTransferOptions? ServerOptions)
    {
        public static RealmTransferRequest ToServer(int playerId, ServerContext target, RealmEntry? entry = null, RealmExit? exit = null, ServerTransferOptions? options = null)
            => new(playerId, RealmTarget.ForServer(target), entry ?? RealmEntry.Default, exit ?? RealmExit.Default, options);

        public static RealmTransferRequest ToRealm(int playerId, RealmPlan target, RealmEntry? entry = null, RealmExit? exit = null, ServerTransferOptions? options = null)
            => new(playerId, RealmTarget.ForPlan(target), entry ?? RealmEntry.Default, exit ?? RealmExit.Default, options);
    }
}
