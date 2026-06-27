namespace Kaleido.Systems
{
    public sealed class RealmJoinPipeline(RealmSystemScope scope)
    {
        public IDisposable Use(RealmJoinHandler handler, int priority = 0) {
            ArgumentNullException.ThrowIfNull(handler);
            return scope.RegisterJoinHandler(handler, priority);
        }
    }
}
