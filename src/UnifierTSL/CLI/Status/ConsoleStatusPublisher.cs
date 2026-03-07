namespace UnifierTSL.CLI.Status
{
    internal readonly record struct ConsoleStatusPublication(
        long Sequence,
        bool HasFrame,
        ConsoleStatusFrame Frame);

    internal sealed class ConsoleStatusPublisher : IDisposable
    {
        private readonly ConsoleStatusService statusService;
        private readonly Action<ConsoleStatusPublication> sink;
        private readonly Func<bool> shouldPublish;
        private readonly Timer timer;
        private long nextSequence;
        private int disposed;

        public ConsoleStatusPublisher(
            ConsoleStatusService statusService,
            Action<ConsoleStatusPublication> sink,
            Func<bool>? shouldPublish = null) {
            this.statusService = statusService ?? throw new ArgumentNullException(nameof(statusService));
            this.sink = sink ?? throw new ArgumentNullException(nameof(sink));
            this.shouldPublish = shouldPublish ?? (() => true);
            timer = new Timer(
                static state => ((ConsoleStatusPublisher)state!).PublishIfChanged(),
                this,
                ConsoleStatusService.RefreshIntervalMs,
                ConsoleStatusService.RefreshIntervalMs);
        }

        public void PublishIfChanged() {
            if (Volatile.Read(ref disposed) != 0 || !shouldPublish()) {
                return;
            }

            try {
                if (!statusService.TryGetTopFrameIfChanged(out ConsoleStatusFrame frame, out bool hasFrame)) {
                    return;
                }

                Publish(hasFrame, frame);
            }
            catch (ObjectDisposedException) {
            }
            catch {
            }
        }

        public void ReplayCurrent() {
            if (Volatile.Read(ref disposed) != 0 || !shouldPublish()) {
                return;
            }

            try {
                bool hasFrame = statusService.TryPeekTopFrame(out ConsoleStatusFrame frame);
                Publish(hasFrame, frame);
            }
            catch (ObjectDisposedException) {
            }
            catch {
            }
        }

        public void Dispose() {
            if (Interlocked.Exchange(ref disposed, 1) != 0) {
                return;
            }

            try {
                timer.Dispose();
            }
            catch {
            }
        }

        private void Publish(bool hasFrame, ConsoleStatusFrame frame) {
            long sequence = Interlocked.Increment(ref nextSequence);
            sink(new ConsoleStatusPublication(sequence, hasFrame, frame));
        }
    }
}
