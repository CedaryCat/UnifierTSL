using Kaleido.Model.Planning;
using Kaleido.Model.Transfer;
using UnifierTSL.Servers;

namespace Kaleido.Systems
{
    public sealed record RealmJoinDecision(
        RealmTarget Target,
        RealmEntry Entry)
    {
        public static RealmJoinDecision ToRealm(RealmPlan plan, RealmEntry? entry = null)
            => new(RealmTarget.ForPlan(plan), entry ?? RealmEntry.Default);

        public static RealmJoinDecision ToServer(ServerContext server, RealmEntry? entry = null)
            => new(RealmTarget.ForServer(server), entry ?? RealmEntry.Default);
    }
}
