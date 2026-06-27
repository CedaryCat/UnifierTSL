namespace Kaleido.Systems
{
    public sealed class RealmInstanceRetiringEvent(RealmSystemScope scope)
    {
        public IDisposable Register(RealmEventHandler<RealmInstanceRetiring> handler) {
            ArgumentNullException.ThrowIfNull(handler);
            return scope.RegisterInstanceRetiringHandler(handler);
        }
    }
}
