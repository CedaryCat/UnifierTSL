using UnifierTSL.Surface.Activities;
using UnifierTSL.Surface.Hosting;
using UnifierTSL.Surface.Status;

namespace TShockAPI.Commanding
{
    internal sealed class TSConsoleActivityScope : IDisposable, IAsyncDisposable
    {
        private readonly IDisposable? innerScope;
        private int disposed;

        private TSConsoleActivityScope(
            ActivityHandle? activity,
            IDisposable? innerScope,
            CancellationToken cancellationToken) {
            Activity = activity;
            this.innerScope = innerScope;
            CancellationToken = activity?.CancellationToken ?? cancellationToken;
        }
        public static TSConsoleActivityScope None => new(activity: null, innerScope: null, cancellationToken: default);
        public ActivityHandle? Activity { get; }
        public CancellationToken CancellationToken { get; }
        public static TSConsoleActivityScope Begin(
            CommandExecutor executor,
            string category,
            string message,
            ActivityDisplayOptions display = default,
            CancellationToken cancellationToken = default) {

            if (executor.IsClient || executor.IsRest) {
                return None;
            }

            if (executor.SourceServer is not null) {
                var activity = executor.SourceServer.Console.BeginSurfaceActivity(
                    category,
                    message,
                    display,
                    cancellationToken);
                return new TSConsoleActivityScope(activity, activity, cancellationToken);
            }

            var launcherScope = Console.BeginSurfaceActivity(
                category,
                message,
                display,
                cancellationToken);
            return new TSConsoleActivityScope(
                launcherScope.Activity,
                launcherScope,
                launcherScope.CancellationToken);
        }
        public void Dispose() {
            if (Interlocked.Exchange(ref disposed, 1) != 0) {
                return;
            }

            try {
                innerScope?.Dispose();
            }
            catch {
            }
        }
        public ValueTask DisposeAsync() {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
