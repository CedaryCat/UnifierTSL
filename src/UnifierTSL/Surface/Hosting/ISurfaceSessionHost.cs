namespace UnifierTSL.Surface.Hosting {
    public interface ISurfaceSessionHost {
        ISurfaceSession CreateSession(SurfaceSessionOptions options);
    }
}
