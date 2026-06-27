using System.Collections.Immutable;
using Terraria;
using UnifierTSL.Servers;

namespace Kaleido.LoginLobby
{
    public readonly record struct LoginLobbyDestinationContext(
        Player Player,
        RemoteClient Client,
        ImmutableArray<ServerContext> CandidateServers);
}
