namespace Kaleido.Systems
{
    public sealed class RealmSystemMaintenance(RealmSystemScope scope)
    {
        public IDisposable Every(TimeSpan interval, RealmMaintenanceCallback callback) {
            ArgumentNullException.ThrowIfNull(callback);
            if (interval <= TimeSpan.Zero) {
                throw new ArgumentOutOfRangeException(nameof(interval), interval, "Maintenance interval must be positive.");
            }

            return scope.RegisterMaintenance(interval, callback);
        }
    }
}
