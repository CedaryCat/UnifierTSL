namespace UnifierTSL.Surface.Hosting {
    public enum SurfaceSessionLifetimeMode : byte {
        HostOwned,
        UserReleasable,
    }

    public sealed class SurfaceSessionOptions(SurfaceSessionLifetimeMode lifetimeMode = SurfaceSessionLifetimeMode.HostOwned) {
        public static SurfaceSessionOptions HostOwned { get; } = new(SurfaceSessionLifetimeMode.HostOwned);
        public static SurfaceSessionOptions UserReleasable { get; } = new(SurfaceSessionLifetimeMode.UserReleasable);

        public SurfaceSessionLifetimeMode LifetimeMode { get; } = lifetimeMode;
    }
}
