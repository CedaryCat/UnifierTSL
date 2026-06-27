using System.Collections.Immutable;
using Terraria;
using Terraria.Net.Sockets;
using UnifierTSL.Events.Core;
using UnifierTSL.Events.Handlers;
using UnifierTSL.Network;

namespace UnifierTSL.Servers
{
    public sealed record ServerTransferOptions(
        bool RequireRunning = true,
        bool AllowTransientTarget = false,
        bool SyncSections = true,
        bool RaiseEvents = true)
    {
        public static ServerTransferOptions Default { get; } = new();
        public static ServerTransferOptions TransientTarget { get; } = new(RequireRunning: false, AllowTransientTarget: true);
    }

    public sealed record ServerTransferRequest(int PlayerId, ServerContext Target, ServerTransferOptions? Options = null);

    public sealed record ServerTransferResult(
        bool Succeeded,
        int PlayerId,
        ServerContext? Source,
        ServerContext? Target,
        string? Error)
    {
        public static ServerTransferResult Success(int playerId, ServerContext? source, ServerContext target)
            => new(true, playerId, source, target, null);

        public static ServerTransferResult Failure(int playerId, ServerContext? source, ServerContext? target, string error)
            => new(false, playerId, source, target, error);
    }

    public sealed record ServerJoinRequest(Player Player, RemoteClient Client, ImmutableArray<ServerContext> CandidateServers);

    public sealed record ServerTransferNotification(int PlayerId, ServerContext Source, ServerContext Target);

    public sealed record ServerLeaveNotification(int PlayerId, ServerContext Server);

    public interface IServerJoinResolver
    {
        ServerContext? ResolveJoin(ServerJoinRequest request);
    }

    public interface IServerTransferObserver
    {
        void OnTransferred(ServerTransferNotification transfer);
    }

    public interface IServerLeaveObserver
    {
        void OnLeft(ServerLeaveNotification leave);
    }

    public static class ServerRuntime
    {
        public static ImmutableArray<ServerContext> Servers => UnifiedServerCoordinator.Servers;
        public static bool AnyClientsConnected => UnifiedServerCoordinator.AnyClientsConnected;
        public static RemoteClient[] Clients => UnifiedServerCoordinator.globalClients;
        public static LocalClientSender[] Senders => UnifiedServerCoordinator.clientSenders;
        public static MessageBuffer[] MessageBuffers => UnifiedServerCoordinator.globalMsgBuffers;

        public static ServerContext? GetCurrentServer(int playerId) => UnifiedServerCoordinator.GetClientCurrentlyServer(playerId);

        public static Player GetPlayer(int playerId) => UnifiedServerCoordinator.GetPlayer(playerId);

        public static LocalClientSender GetSender(int playerId) => UnifiedServerCoordinator.clientSenders[playerId];

        public static Task<ServerTransferResult> TransferAsync(ServerTransferRequest request, CancellationToken cancellationToken = default)
            => ServerTransferCoordinator.TransferAsync(request, cancellationToken);

        public static void Register(ServerContext server) => UnifiedServerCoordinator.AddServer(server);

        public static void Unregister(ServerContext server) => UnifiedServerCoordinator.RemoveServer(server);

        public static IDisposable RegisterJoinResolver(IServerJoinResolver resolver) {
            ArgumentNullException.ThrowIfNull(resolver);
            return new JoinResolverSubscription(resolver);
        }

        public static IDisposable RegisterTransferObserver(IServerTransferObserver observer) {
            ArgumentNullException.ThrowIfNull(observer);
            return new TransferObserverSubscription(observer);
        }

        public static IDisposable RegisterLeaveObserver(IServerLeaveObserver observer) {
            ArgumentNullException.ThrowIfNull(observer);
            return new LeaveObserverSubscription(observer);
        }

        private sealed class JoinResolverSubscription : IDisposable
        {
            private readonly IServerJoinResolver resolver;
            private bool disposed;

            public JoinResolverSubscription(IServerJoinResolver resolver) {
                this.resolver = resolver;
                UnifierApi.EventHub.Coordinator.SwitchJoinServer.Register(OnSwitchJoinServer, HandlerPriority.VeryHigh);
            }

            private void OnSwitchJoinServer(ref ValueEventNoCancelArgs<SwitchJoinServerEvent> args) {
                if (args.Content.JoinServer is not null) {
                    return;
                }

                var target = resolver.ResolveJoin(new(args.Content.Player, args.Content.Client, args.Content.Servers));
                if (target is null) {
                    return;
                }

                args.Content.JoinServer = target;
                args.StopPropagation = true;
            }

            public void Dispose() {
                if (disposed) {
                    return;
                }

                disposed = true;
                UnifierApi.EventHub.Coordinator.SwitchJoinServer.UnRegister(OnSwitchJoinServer);
            }
        }

        private sealed class TransferObserverSubscription : IDisposable
        {
            private readonly IServerTransferObserver observer;
            private bool disposed;

            public TransferObserverSubscription(IServerTransferObserver observer) {
                this.observer = observer;
                UnifierApi.EventHub.Coordinator.PostServerTransfer.Register(OnPostServerTransfer, HandlerPriority.Normal);
            }

            private void OnPostServerTransfer(ref ReadonlyNoCancelEventArgs<PostServerTransferEvent> args) {
                observer.OnTransferred(new(args.Content.Who, args.Content.From, args.Content.Server));
            }

            public void Dispose() {
                if (disposed) {
                    return;
                }

                disposed = true;
                UnifierApi.EventHub.Coordinator.PostServerTransfer.UnRegister(OnPostServerTransfer);
            }
        }

        private sealed class LeaveObserverSubscription : IDisposable
        {
            private readonly IServerLeaveObserver observer;
            private bool disposed;

            public LeaveObserverSubscription(IServerLeaveObserver observer) {
                this.observer = observer;
                UnifierApi.EventHub.Netplay.LeaveEvent.Register(OnLeave, HandlerPriority.Normal);
            }

            private void OnLeave(ref ReadonlyNoCancelEventArgs<LeaveEvent> args) {
                observer.OnLeft(new(args.Content.Who, args.Content.Server));
            }

            public void Dispose() {
                if (disposed) {
                    return;
                }

                disposed = true;
                UnifierApi.EventHub.Netplay.LeaveEvent.UnRegister(OnLeave);
            }
        }
    }
}
