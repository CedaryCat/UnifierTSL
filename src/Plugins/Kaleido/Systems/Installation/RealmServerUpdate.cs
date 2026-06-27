using Kaleido.Model.Instances;
using UnifierTSL.Servers;

namespace Kaleido.Systems.Installation
{
    public readonly record struct RealmServerUpdate(RealmInstance Instance, ServerContext Server);
}
