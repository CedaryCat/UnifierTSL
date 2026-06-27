using Kaleido.Model.Transfer;

namespace Kaleido.Systems.Installation
{
    public sealed class RealmInstallLifetime(RealmInstallScope scope)
    {
        public IDisposable Track(IDisposable registration) => scope.Track(registration);

        public RealmHold Hold(TimeSpan? duration = null) {
            var hold = scope.Instance.Hold(duration);
            scope.Track(hold);
            return hold;
        }
    }
}
