using Kaleido.Model.Instances;
using Kaleido.Model.Lifecycle;

namespace Kaleido.Systems
{
    public sealed record RealmInstanceRetiring(
        RealmInstance Instance,
        RealmRetireReason Reason);
}
