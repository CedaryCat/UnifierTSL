using UnifierTSL;
using UnifierTSL.Events.Core;
using UnifierTSL.Events.Handlers;

namespace Kaleido.Systems.Installation
{
    public sealed class RealmRuntimeHooks(RealmInstallScope scope)
    {
        public IDisposable OnPreUpdate(Action<RealmServerUpdate> handler, HandlerPriority priority = HandlerPriority.Normal)
            => Register(UnifierApi.EventHub.Game.PreUpdate, handler, priority);

        public IDisposable OnPostUpdate(Action<RealmServerUpdate> handler, HandlerPriority priority = HandlerPriority.Normal)
            => Register(UnifierApi.EventHub.Game.PostUpdate, handler, priority);

        private IDisposable Register(
            ReadonlyEventNoCancelProvider<ServerEvent> events,
            Action<RealmServerUpdate> handler,
            HandlerPriority priority) {

            ArgumentNullException.ThrowIfNull(handler);
            ReadonlyEventNoCancelDelegate<ServerEvent> callback = (ref ReadonlyNoCancelEventArgs<ServerEvent> args) => {
                if (ReferenceEquals(args.Content.Server, scope.Server)) {
                    handler(new(scope.Instance, scope.Server));
                }
            };
            events.Register(callback, priority);
            return scope.Track(new EventRegistration(() => events.UnRegister(callback)));
        }
    }
}
