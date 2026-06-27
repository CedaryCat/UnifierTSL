using TrProtocol;
using TrProtocol.Models;
using UnifierTSL.Events.Handlers;
using UnifierTSL.Network;

namespace Kaleido.Hosting.SharedProjection
{
    public enum ProjectionInputResult
    {
        Unhandled,
        Handled,
        Forward
    }

    public readonly record struct ProjectionPacket<TPacket>(
        SharedProjectionContext Context,
        LocalClientSender Remote,
        TPacket Packet)
        where TPacket : struct, INetPacket;

    public readonly record struct ProjectionModule<TModule>(
        SharedProjectionContext Context,
        LocalClientSender Remote,
        TModule Module)
        where TModule : struct, INetPacket;

    public readonly record struct ProjectionPlayerEntered(
        SharedProjectionContext Context,
        LocalClientSender Remote);

    public readonly record struct ProjectionPlayerSync(
        SharedProjectionContext Context,
        LocalClientSender Remote);

    public readonly record struct ProjectionPlayerFrame(
        SharedProjectionContext Context,
        int PlayerId);

    internal sealed class ProjectionInput
    {
        private readonly SharedProjectionContext context;
        private readonly Dictionary<MessageID, List<IPacketHandler>> packets = [];
        private readonly Dictionary<NetModuleType, List<IModuleHandler>> modules = [];
        private readonly List<Action<ProjectionPlayerEntered>> entered = [];
        private readonly List<Action<ProjectionPlayerSync>> syncing = [];
        private readonly List<Action<ProjectionPlayerFrame>> frames = [];
        private readonly Lock gate = new();

        public ProjectionInput(SharedProjectionContext context) {
            this.context = context;
        }

        public IDisposable RegisterPacket<TPacket>(MessageID packetType, Func<ProjectionPacket<TPacket>, ProjectionInputResult> handler)
            where TPacket : struct, INetPacket {

            ArgumentNullException.ThrowIfNull(handler);
            var registration = new PacketHandler<TPacket>(context, handler);
            lock (gate) {
                if (!packets.TryGetValue(packetType, out var handlers)) {
                    packets.Add(packetType, handlers = []);
                }
                handlers.Add(registration);
            }
            return new Registration(() => RemovePacket(packetType, registration));
        }

        public IDisposable RegisterModule<TModule>(NetModuleType moduleType, Func<ProjectionModule<TModule>, ProjectionInputResult> handler)
            where TModule : struct, INetPacket {

            ArgumentNullException.ThrowIfNull(handler);
            var registration = new ModuleHandler<TModule>(context, handler);
            lock (gate) {
                if (!modules.TryGetValue(moduleType, out var handlers)) {
                    modules.Add(moduleType, handlers = []);
                }
                handlers.Add(registration);
            }
            return new Registration(() => RemoveModule(moduleType, registration));
        }

        public IDisposable RegisterEntered(Action<ProjectionPlayerEntered> handler) {
            ArgumentNullException.ThrowIfNull(handler);
            lock (gate) {
                entered.Add(handler);
            }
            return new Registration(() => Remove(entered, handler));
        }

        public IDisposable RegisterSync(Action<ProjectionPlayerSync> handler) {
            ArgumentNullException.ThrowIfNull(handler);
            lock (gate) {
                syncing.Add(handler);
            }
            return new Registration(() => Remove(syncing, handler));
        }

        public IDisposable RegisterFrame(Action<ProjectionPlayerFrame> handler) {
            ArgumentNullException.ThrowIfNull(handler);
            lock (gate) {
                frames.Add(handler);
            }
            return new Registration(() => Remove(frames, handler));
        }

        public ProjectionInputResult RoutePacket(MessageID packetType, LocalClientSender remote, ref readonly ReceiveBytesInfo info) {
            IPacketHandler[] snapshot;
            lock (gate) {
                if (!packets.TryGetValue(packetType, out var handlers)) {
                    return ProjectionInputResult.Unhandled;
                }
                snapshot = [.. handlers];
            }

            foreach (var handler in snapshot) {
                var result = handler.Invoke(remote, in info);
                if (result != ProjectionInputResult.Unhandled) {
                    return result;
                }
            }
            return ProjectionInputResult.Unhandled;
        }

        public ProjectionInputResult RouteModule(NetModuleType moduleType, LocalClientSender remote, ref readonly ReceiveBytesInfo info) {
            IModuleHandler[] snapshot;
            lock (gate) {
                if (!modules.TryGetValue(moduleType, out var handlers)) {
                    return ProjectionInputResult.Unhandled;
                }
                snapshot = [.. handlers];
            }

            foreach (var handler in snapshot) {
                var result = handler.Invoke(remote, in info);
                if (result != ProjectionInputResult.Unhandled) {
                    return result;
                }
            }
            return ProjectionInputResult.Unhandled;
        }

        public void InvokeEntered(LocalClientSender remote) {
            Action<ProjectionPlayerEntered>[] snapshot;
            lock (gate) {
                snapshot = [.. entered];
            }
            Invoke(snapshot, new ProjectionPlayerEntered(context, remote), "player-entered");
        }

        public void InvokeSync(LocalClientSender remote) {
            Action<ProjectionPlayerSync>[] snapshot;
            lock (gate) {
                snapshot = [.. syncing];
            }
            Invoke(snapshot, new ProjectionPlayerSync(context, remote), "player-sync");
        }

        public void InvokeFrame(int playerId) {
            Action<ProjectionPlayerFrame>[] snapshot;
            lock (gate) {
                snapshot = [.. frames];
            }
            Invoke(snapshot, new ProjectionPlayerFrame(context, playerId), "player-frame");
        }

        private void RemovePacket(MessageID packetType, IPacketHandler handler) {
            lock (gate) {
                if (!packets.TryGetValue(packetType, out var handlers)) {
                    return;
                }
                handlers.Remove(handler);
                if (handlers.Count == 0) {
                    packets.Remove(packetType);
                }
            }
        }

        private void RemoveModule(NetModuleType moduleType, IModuleHandler handler) {
            lock (gate) {
                if (!modules.TryGetValue(moduleType, out var handlers)) {
                    return;
                }
                handlers.Remove(handler);
                if (handlers.Count == 0) {
                    modules.Remove(moduleType);
                }
            }
        }

        private void Remove<T>(List<T> handlers, T handler) {
            lock (gate) {
                handlers.Remove(handler);
            }
        }

        private void Invoke<T>(Action<T>[] handlers, T input, string kind) {
            foreach (var handler in handlers) {
                try {
                    handler(input);
                }
                catch (Exception ex) {
                    LogFailure(kind, ex);
                }
            }
        }

        private void LogFailure(string kind, Exception ex) {
            context.Log.Error(
                category: "SharedProjection",
                message: $"Shared projection '{context.Name}' {kind} handler failed.",
                ex: ex);
        }

        private interface IPacketHandler
        {
            ProjectionInputResult Invoke(LocalClientSender remote, ref readonly ReceiveBytesInfo info);
        }

        private interface IModuleHandler
        {
            ProjectionInputResult Invoke(LocalClientSender remote, ref readonly ReceiveBytesInfo info);
        }

        private sealed class PacketHandler<TPacket>(SharedProjectionContext context, Func<ProjectionPacket<TPacket>, ProjectionInputResult> handler) : IPacketHandler
            where TPacket : struct, INetPacket
        {
            public ProjectionInputResult Invoke(LocalClientSender remote, ref readonly ReceiveBytesInfo info) {
                try {
                    return handler(new(context, remote, ProjectionPacketReader.ReadPacket<TPacket>(in info)));
                }
                catch (Exception ex) {
                    context.Log.Error(
                        category: "SharedProjection",
                        message: $"Shared projection '{context.Name}' packet handler for {typeof(TPacket).Name} failed.",
                        ex: ex);
                    return ProjectionInputResult.Handled;
                }
            }
        }

        private sealed class ModuleHandler<TModule>(SharedProjectionContext context, Func<ProjectionModule<TModule>, ProjectionInputResult> handler) : IModuleHandler
            where TModule : struct, INetPacket
        {
            public ProjectionInputResult Invoke(LocalClientSender remote, ref readonly ReceiveBytesInfo info) {
                try {
                    return handler(new(context, remote, ProjectionPacketReader.ReadPacket<TModule>(in info, 3)));
                }
                catch (Exception ex) {
                    context.Log.Error(
                        category: "SharedProjection",
                        message: $"Shared projection '{context.Name}' module handler for {typeof(TModule).Name} failed.",
                        ex: ex);
                    return ProjectionInputResult.Handled;
                }
            }
        }

        private sealed class Registration(Action unregister) : IDisposable
        {
            private Action? unregister = unregister;

            public void Dispose() => Interlocked.Exchange(ref unregister, null)?.Invoke();
        }
    }
}
