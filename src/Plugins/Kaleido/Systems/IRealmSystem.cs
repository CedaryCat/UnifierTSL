namespace Kaleido.Systems
{
    public interface IRealmSystem
    {
        string Id { get; }
        Task MountAsync(RealmSystemScope scope, CancellationToken cancellationToken);
    }
}
