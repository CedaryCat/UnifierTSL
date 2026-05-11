using System.Diagnostics;

namespace UnifierTSL.Surface.Activities
{
    public sealed class ActivityHandle : IDisposable, IAsyncDisposable
    {
        private readonly Lock sync = new();
        private readonly Stopwatch stopwatch = Stopwatch.StartNew();
        private readonly string category;
        private readonly Action<ActivityHandle>? onDisposed;
        private readonly Action<ActivityHandle>? onStateChanged;
        private readonly CancellationTokenSource cancellationSource;
        private string message;
        private bool progressEnabled;
        private long progressCurrent;
        private long progressTotal;
        private ActivityProgressStyle progressStyle;
        private bool hideElapsed;
        private int disposed;

        internal ActivityHandle(
            string category,
            string message,
            ActivityDisplayOptions display,
            Action<ActivityHandle>? onDisposed = null,
            Action<ActivityHandle>? onStateChanged = null,
            CancellationToken cancellationToken = default) {
            this.category = NormalizeCategory(category);
            this.message = NormalizeMessage(message);
            progressEnabled = false;
            progressCurrent = 0;
            progressTotal = 0;
            progressStyle = display.ProgressStyle;
            hideElapsed = display.HideElapsed;
            this.onDisposed = onDisposed;
            this.onStateChanged = onStateChanged;
            cancellationSource = cancellationToken.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                : new CancellationTokenSource();
        }

        public string Message {
            get {
                lock (sync) {
                    return message;
                }
            }
            set => UpdateState(value, static (self, nextValue) => self.message = NormalizeMessage(nextValue));
        }

        public bool ProgressEnabled {
            get {
                lock (sync) {
                    return progressEnabled;
                }
            }
            set => UpdateState(value, static (self, nextValue) => self.progressEnabled = nextValue);
        }

        public long ProgressCurrent {
            get {
                lock (sync) {
                    return progressCurrent;
                }
            }
            set => UpdateState(value, static (self, nextValue) => self.progressCurrent = nextValue);
        }

        public long ProgressTotal {
            get {
                lock (sync) {
                    return progressTotal;
                }
            }
            set => UpdateState(value, static (self, nextValue) => self.progressTotal = nextValue);
        }

        public ActivityProgressStyle ProgressStyle {
            get {
                lock (sync) {
                    return progressStyle;
                }
            }
            set => UpdateState(value, static (self, nextValue) => self.progressStyle = nextValue);
        }

        public bool HideElapsed {
            get {
                lock (sync) {
                    return hideElapsed;
                }
            }
            set => UpdateState(value, static (self, nextValue) => self.hideElapsed = nextValue);
        }

        public CancellationToken CancellationToken => cancellationSource.Token;

        public bool IsCancellationRequested => cancellationSource.IsCancellationRequested;

        internal static ActivityHandle CreateNoop(
            string category,
            string message,
            ActivityDisplayOptions display = default,
            CancellationToken cancellationToken = default) {
            return new ActivityHandle(
                category: category,
                message: message,
                display: display,
                cancellationToken: cancellationToken);
        }

        internal ActivityStatusSnapshot? TryCreateStatusSnapshot() {
            lock (sync) {
                if (Volatile.Read(ref disposed) != 0) {
                    return null;
                }

                return new ActivityStatusSnapshot(
                    Category: category,
                    Message: message,
                    ProgressEnabled: progressEnabled,
                    ProgressCurrent: progressCurrent,
                    ProgressTotal: progressTotal,
                    ProgressStyle: progressStyle,
                    HideElapsed: hideElapsed,
                    Elapsed: stopwatch.Elapsed,
                    IsCancellationRequested: cancellationSource.IsCancellationRequested);
            }
        }

        public bool RequestCancel() {
            if (Volatile.Read(ref disposed) != 0 || cancellationSource.IsCancellationRequested) {
                return false;
            }

            try {
                cancellationSource.Cancel();
                onStateChanged?.Invoke(this);
                return true;
            }
            catch (ObjectDisposedException) {
                return false;
            }
        }

        public void Dispose() {
            if (Interlocked.Exchange(ref disposed, 1) != 0) {
                return;
            }

            lock (sync) {
                stopwatch.Stop();
            }

            try {
                cancellationSource.Dispose();
            }
            catch {
            }

            onDisposed?.Invoke(this);
        }

        public ValueTask DisposeAsync() {
            Dispose();
            return ValueTask.CompletedTask;
        }

        private void UpdateState<TValue>(TValue value, Action<ActivityHandle, TValue> updater) {
            lock (sync) {
                if (Volatile.Read(ref disposed) != 0) {
                    return;
                }

                updater(this, value);
            }

            onStateChanged?.Invoke(this);
        }

        private static string NormalizeCategory(string? value) {
            return string.IsNullOrWhiteSpace(value)
                ? "Work"
                : value.Trim();
        }

        private static string NormalizeMessage(string? value) {
            return string.IsNullOrWhiteSpace(value)
                ? "Running..."
                : value.Trim();
        }

    }

    internal readonly record struct ActivityStatusSnapshot(
        string Category,
        string Message,
        bool ProgressEnabled,
        long ProgressCurrent,
        long ProgressTotal,
        ActivityProgressStyle ProgressStyle,
        bool HideElapsed,
        TimeSpan Elapsed,
        bool IsCancellationRequested);

    internal readonly record struct ActivityViewSnapshot(
        ActivityStatusSnapshot SelectedActivity,
        int SelectedIndex,
        string[] ActivityCategories)
    {
        public int ActivityCount => ActivityCategories.Length;
    }
}
