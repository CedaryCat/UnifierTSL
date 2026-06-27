using System.Collections.Immutable;
using Terraria;
using Terraria.Net.Sockets;
using UnifierTSL.Servers;

namespace Kaleido.Systems
{
    public sealed record RealmJoin(
        int PlayerId,
        Player Player,
        RemoteClient Client,
        ImmutableArray<ServerContext> CandidateServers);
}
