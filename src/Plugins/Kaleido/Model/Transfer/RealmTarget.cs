using Kaleido.Model.Planning;
using UnifierTSL.Servers;

namespace Kaleido.Model.Transfer
{
    public sealed record RealmTarget(RealmPlan? Plan, ServerContext? Server)
    {
        public static RealmTarget ForPlan(RealmPlan plan) => new(plan, null);
        public static RealmTarget ForServer(ServerContext server) => new(null, server);
    }
}
