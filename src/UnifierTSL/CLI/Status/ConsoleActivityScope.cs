using System.Diagnostics;
using UnifierTSL.ConsoleClient.Shared.ConsolePrompting;

namespace UnifierTSL.CLI.Status
{
    internal sealed class ConsoleActivityScope : IDisposable
    {
        private const int SpinnerFrameIntervalMs = 120;
        private static readonly string SpinnerIndicatorFramesSerialized = ConsoleStatusIndicatorFramesCodec.Serialize(["|", "/", "-", "\\"]);
        private readonly Lock sync = new();
        private readonly ConsoleStatusService statusService;
        private readonly Stopwatch stopwatch = Stopwatch.StartNew();
        private readonly string category;
        private readonly string message;
        private readonly Action<ConsoleActivityScope>? onDisposed;
        private Timer? timer;
        private ConsoleStatusService.OverlayScope? overlayScope;
        private int disposed;

        public ConsoleActivityScope(
            ConsoleStatusService statusService,
            string category,
            string message,
            Action<ConsoleActivityScope>? onDisposed = null) {
            this.statusService = statusService ?? throw new ArgumentNullException(nameof(statusService));
            this.category = string.IsNullOrWhiteSpace(category) ? "Work" : category.Trim();
            this.message = string.IsNullOrWhiteSpace(message) ? "Running..." : message.Trim();
            this.onDisposed = onDisposed;
        }

        public void Start() {
            lock (sync) {
                if (Volatile.Read(ref disposed) != 0) {
                    return;
                }

                overlayScope = statusService.PushOverlayScope(BuildFrame(0));
                timer = new Timer(
                    static state => ((ConsoleActivityScope)state!).PublishFrame(),
                    this,
                    ConsoleStatusService.RefreshIntervalMs,
                    ConsoleStatusService.RefreshIntervalMs);
            }
        }

        public void Dispose() {
            Timer? localTimer = null;
            ConsoleStatusService.OverlayScope? localOverlay = null;
            if (Interlocked.Exchange(ref disposed, 1) != 0) {
                return;
            }

            lock (sync) {
                localTimer = timer;
                timer = null;
                localOverlay = overlayScope;
                overlayScope = null;
                stopwatch.Stop();
            }

            try {
                localTimer?.Dispose();
            }
            catch {
            }

            try {
                localOverlay?.Dispose();
            }
            catch {
            }

            onDisposed?.Invoke(this);
        }

        private void PublishFrame() {
            lock (sync) {
                if (Volatile.Read(ref disposed) != 0) {
                    return;
                }

                overlayScope?.Update(BuildFrame(stopwatch.ElapsedMilliseconds));
            }
        }

        private ConsoleStatusFrame BuildFrame(long elapsedMs) {
            return new ConsoleStatusFrame(
                Text: $"ConsoleActivity:{category} task:{message} elapsed:{elapsedMs:0} ms",
                IndicatorFrameIntervalMs: SpinnerFrameIntervalMs,
                IndicatorFrames: SpinnerIndicatorFramesSerialized);
        }
    }
}
