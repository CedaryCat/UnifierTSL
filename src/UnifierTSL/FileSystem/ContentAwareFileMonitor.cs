using System.Security.Cryptography;

namespace UnifierTSL.FileSystem
{
    /// <summary>
    /// Keep the original ContentAwareFileWatcher content awareness logic, but don't own the FileSystemWatcher.
    /// Provided events by the upper layer through HandleExternalFsEvent.
    /// </summary>
    internal sealed class ContentAwareFileMonitor : IDisposable
    {
        private readonly string _filePath;
        private readonly Lock _sync = new();
        private string? _lastHash;
        private readonly TimeSpan _debounceInterval = TimeSpan.FromMilliseconds(300);
        private DateTime _lastEventTime = DateTime.MinValue;
        volatile bool _handlingScheduled = false;

        volatile int _pendingInternalWriteVersion = 0;
        volatile int _lastInternalWriteVersionAcknowledged = 0;

        private bool _disposed = false;

        public event FileSystemEventHandler? Modified;
        public event ErrorEventHandler? Error;

        public bool HasSubscribers => Modified != null || Error != null;

        public ContentAwareFileMonitor(string filePath) {
            _filePath = Path.GetFullPath(filePath);
            var dir = Path.GetDirectoryName(_filePath)!;
            if (!Directory.Exists(dir)) {
                Directory.CreateDirectory(dir);
            }

            if (File.Exists(_filePath)) {
                _lastHash = TryComputeHashWithRetry();
            }
        }

        /// <summary>
        /// Provided events by the upper layer (essentially "external" events).
        /// </summary>
        public void HandleExternalFsEvent(FileSystemEventArgs e) {
            if (!IsTargetPath(e.FullPath))
                return;

            ScheduleHandleContentChange();
        }

        private bool IsTargetPath(string path) {
            return string.Equals(Path.GetFullPath(path), _filePath, StringComparison.OrdinalIgnoreCase);
        }

        private void ScheduleHandleContentChange() {
            lock (_sync) {
                _lastEventTime = DateTime.UtcNow;
                if (_handlingScheduled)
                    return;
                _handlingScheduled = true;
            }

            _ = Task.Run(async () => {
                while (true) {
                    DateTime snapshot;
                    lock (_sync) {
                        snapshot = _lastEventTime;
                    }

                    await Task.Delay(_debounceInterval);

                    bool shouldContinue;
                    lock (_sync) {
                        shouldContinue = snapshot != _lastEventTime;
                        if (!shouldContinue) {
                            _handlingScheduled = false;
                        }
                    }

                    if (shouldContinue)
                        continue;

                    await DebouncedHandler();
                    break;
                }
            });
        }

        private async Task DebouncedHandler() {
            string? newHash = TryComputeHashWithRetry();
            if (newHash == null) {
                await Task.Delay(200);
                newHash = TryComputeHashWithRetry();
            }

            if (newHash == null)
                return;

            lock (_sync) {
                if (_pendingInternalWriteVersion > _lastInternalWriteVersionAcknowledged) {
                    return; // Internal write has already been seen
                }

                if (_lastHash == newHash) {
                    return; // Content hasn't changed
                }

                _lastHash = newHash;
                _lastInternalWriteVersionAcknowledged = _pendingInternalWriteVersion;
            }

            HandleContentChanged();
        }

        public async Task WriteInternallyAsync(Func<Task> writeAction) {
            var newVersion = Interlocked.Increment(ref _pendingInternalWriteVersion);
            try {
                await writeAction();
                var newHash = TryComputeHashWithRetry();
                if (newHash != null) {
                    lock (_sync) {
                        _lastHash = newHash;
                        _lastInternalWriteVersionAcknowledged = newVersion;
                    }
                }
            }
            catch (Exception ex) {
                Error?.Invoke(this, new ErrorEventArgs(ex));
                throw;
            }
        }

        public void WriteInternally(Action writeAction) {
            var newVersion = Interlocked.Increment(ref _pendingInternalWriteVersion);
            try {
                writeAction();
                var newHash = TryComputeHashWithRetry();
                if (newHash != null) {
                    lock (_sync) {
                        _lastHash = newHash;
                        _lastInternalWriteVersionAcknowledged = newVersion;
                    }
                }
            }
            catch (Exception ex) {
                Error?.Invoke(this, new ErrorEventArgs(ex));
                throw;
            }
        }

        private string? TryComputeHashWithRetry(int attempts = 3, int delayMs = 100) {
            for (int i = 0; i < attempts; i++) {
                try {
                    if (!File.Exists(_filePath))
                        return null;

                    using var fs = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var sha = SHA256.Create();
                    var hash = sha.ComputeHash(fs);
                    return Convert.ToBase64String(hash);
                }
                catch (IOException) {
                    Thread.Sleep(delayMs);
                }
                catch (UnauthorizedAccessException) {
                    Thread.Sleep(delayMs);
                }
            }

            return null;
        }

        private void HandleContentChanged() {
            Modified?.Invoke(
                this,
                new FileSystemEventArgs(WatcherChangeTypes.Changed,
                    Path.GetDirectoryName(_filePath)!,
                    Path.GetFileName(_filePath)));
        }

        public void Dispose() {
            if (_disposed) return;
            _disposed = true;

            Modified = null;
            Error = null;
        }
    }

}
