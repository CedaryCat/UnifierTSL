namespace UnifierTSL.Surface.Adapter.Cli.Sessions {
    public sealed class LauncherSurfaceSessionHost() : PipeSurfaceSessionHost(CreatePipeNamePrefix()) {
        private static string CreatePipeNamePrefix() {
            return $"USP_Surface_Launcher_{Environment.ProcessId}";
        }
    }
}
