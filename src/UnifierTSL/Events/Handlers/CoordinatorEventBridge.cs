using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using Terraria;
using Terraria.Localization;
using Terraria.Net.Sockets;
using UnifierTSL.Events.Core;
using UnifierTSL.Servers;

namespace UnifierTSL.Events.Handlers
{
    public struct LastPlayerLeftEvent : IEventContent { }
    public struct SwitchJoinServerEvent(Player player, RemoteClient client, ImmutableArray<ServerContext> servers) : IEventContent
    {
        public readonly Player Player = player;
        public readonly RemoteClient Client = client;
        public ImmutableArray<ServerContext> Servers = servers;
        public ServerContext? JoinServer;
    }
    public struct ServerCheckPlayerCanJoinIn(Player player, RemoteClient client, ServerContext server) : IServerEventContent
    {
        public readonly Player Player = player;
        public readonly RemoteClient Client = client;
        public readonly ServerContext Server { get; init; } = server;
        public bool CanJoin = true;
        public NetworkText? FailReason;
    }
    public struct CheckVersion(RemoteClient client, string curRelease, bool canJoin) : IEventContent
    {
        public RemoteClient Client { get; init; } = client;
        public string CurRelease { get; init; } = curRelease;
        public bool CanJoin = canJoin;
    }
    public readonly struct JoinServerEvent(ServerContext server, int who) : IPlayerEventContent
    {
        public ServerContext Server { get; init; } = server;
        public int Who { get; init; } = who;
    }
    public struct CreateSocketEvent(TcpClient client) : IEventContent
    {
        public readonly TcpClient Client = client;
        public ISocket? Socket;
    }
    public struct StartedEvent : IEventContent { }
    public readonly struct PreServerTransferEvent(ServerContext server, ServerContext target, int who) : IPlayerEventContent
    {
        public ServerContext Server { get; init; } = server;
        public ServerContext Target { get; init; } = target;
        public int Who { get; init; } = who;
    }
    public readonly struct PostServerTransferEvent(ServerContext from, ServerContext server, int who) : IPlayerEventContent
    {
        public ServerContext From { get; init; } = from;
        public ServerContext Server { get; init; } = server;
        public int Who { get; init; } = who;
    }
    public class CoordinatorEventBridge
    {
        public readonly ValueEventNoCancelProvider<CheckVersion> CheckVersion = new();
        public readonly ValueEventNoCancelProvider<SwitchJoinServerEvent> SwitchJoinServer = new();
        public readonly ValueEventNoCancelProvider<ServerCheckPlayerCanJoinIn> ServerCheckPlayerCanJoinIn = new();
        public readonly ReadonlyEventNoCancelProvider<JoinServerEvent> JoinServer = new();
        public readonly ValueEventNoCancelProvider<CreateSocketEvent> CreateSocket = new();
        public readonly ReadonlyEventNoCancelProvider<StartedEvent> Started = new();
        public readonly ReadonlyEventProvider<PreServerTransferEvent> PreServerTransfer = new();
        public readonly ReadonlyEventNoCancelProvider<PostServerTransferEvent> PostServerTransfer = new();
        public readonly ReadonlyEventNoCancelProvider<LastPlayerLeftEvent> LastPlayerLeftEvent = new();
    }
}
