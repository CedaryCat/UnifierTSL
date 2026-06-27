namespace Kaleido.Systems.Installation
{
    public interface IRealmContentInstaller
    {
        Task InstallAsync(RealmInstallScope install, CancellationToken cancellationToken);
        Task UninstallAsync(RealmInstallScope install);
    }
}
