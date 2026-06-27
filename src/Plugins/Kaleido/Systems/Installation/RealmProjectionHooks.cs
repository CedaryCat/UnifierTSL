using Kaleido.Hosting.SharedProjection;
using TrProtocol;
using TrProtocol.Models;

namespace Kaleido.Systems.Installation
{
    public sealed class RealmProjectionHooks
    {
        private readonly RealmInstallScope scope;
        private readonly SharedProjectionContext context;

        internal RealmProjectionHooks(RealmInstallScope scope, SharedProjectionContext context) {
            this.scope = scope;
            this.context = context;
        }

        public IDisposable OnPacket<TPacket>(MessageID packetType, Func<ProjectionPacket<TPacket>, ProjectionInputResult> handler)
            where TPacket : struct, INetPacket
            => scope.Track(context.Input.RegisterPacket(packetType, handler));

        public IDisposable OnModule<TModule>(NetModuleType moduleType, Func<ProjectionModule<TModule>, ProjectionInputResult> handler)
            where TModule : struct, INetPacket
            => scope.Track(context.Input.RegisterModule(moduleType, handler));

        public IDisposable OnPlayerEntered(Action<ProjectionPlayerEntered> handler)
            => scope.Track(context.Input.RegisterEntered(handler));

        public IDisposable OnPlayerSync(Action<ProjectionPlayerSync> handler)
            => scope.Track(context.Input.RegisterSync(handler));

        public IDisposable OnPlayerFrame(Action<ProjectionPlayerFrame> handler)
            => scope.Track(context.Input.RegisterFrame(handler));
    }
}
