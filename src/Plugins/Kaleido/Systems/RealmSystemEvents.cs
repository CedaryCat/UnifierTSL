namespace Kaleido.Systems
{
    public sealed class RealmSystemEvents(RealmSystemScope scope)
    {
        public RealmInstanceRetiringEvent InstanceRetiring { get; } = new(scope);
    }
}
