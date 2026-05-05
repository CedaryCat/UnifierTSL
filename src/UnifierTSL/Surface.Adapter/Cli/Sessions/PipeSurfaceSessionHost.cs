using UnifierTSL.Surface.Hosting;

namespace UnifierTSL.Surface.Adapter.Cli.Sessions {
    public abstract class PipeSurfaceSessionHost(string pipeNamePrefix) : ISurfaceSessionHost {
        private static long nextHostId;
        private readonly string pipeNamePrefix = string.IsNullOrWhiteSpace(pipeNamePrefix)
            ? throw new ArgumentException(GetString("A pipe name prefix must be provided."), nameof(pipeNamePrefix))
            : $"{pipeNamePrefix}_{Interlocked.Increment(ref nextHostId)}";
        private long nextSessionId;

        public ISurfaceSession CreateSession(SurfaceSessionOptions options) {
            ArgumentNullException.ThrowIfNull(options);
            var pipeName = $"{pipeNamePrefix}_{Interlocked.Increment(ref nextSessionId)}";
            return new PipeSurfaceSessionDriver(new SurfaceClientProcessLauncher(pipeName), options);
        }
    }
}
