using System.Collections.Concurrent;

namespace UnifierTSL.FileSystem
{
    /// <summary>
    /// Manages multiple content-aware file monitors for a directory. The directory is stable, and there is only one FileSystemWatcher.
    /// </summary>
    public sealed class DirectoryWatchManager : IDisposable
    {
        private readonly string _rootDirectory;
        private readonly FileSystemWatcher _watcher;
        private readonly ConcurrentDictionary<string, ContentAwareFileMonitor> _monitors = new(StringComparer.OrdinalIgnoreCase);
        private bool _disposed;

        public event ErrorEventHandler? Error;

        public DirectoryWatchManager(string rootDirectory) {
            _rootDirectory = Path.GetFullPath(rootDirectory);
            if (!Directory.Exists(_rootDirectory))
                Directory.CreateDirectory(_rootDirectory);

            _watcher = new FileSystemWatcher(_rootDirectory) {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime | NotifyFilters.FileName,
                Filter = "*", // all; per-file filtering happens in monitors
                EnableRaisingEvents = true
            };
            _watcher.Changed += OnFsEvent;
            _watcher.Created += OnFsEvent;
            _watcher.Renamed += OnRenamed;
            _watcher.Deleted += OnFsEvent; // in case file disappears and reappears
            _watcher.Error += (s, e) => Error?.Invoke(s, e);
        }

        /// <summary>
        /// Register a monitor for a specific file. Returns a handle which can be used to unsubscribe or perform internal writes.
        /// </summary>
        public IFileMonitorHandle Register(string filePath, FileSystemEventHandler modified, ErrorEventHandler? error = null) {
            string full = Path.GetFullPath(Path.Combine(_rootDirectory, filePath));
            ContentAwareFileMonitor monitor = _monitors.GetOrAdd(full, fp => new ContentAwareFileMonitor(fp));

            monitor.Modified += modified;

            if (error != null) monitor.Error += error;

            return new Subscription(this, monitor, full, modified, error);
        }

        /// <summary>
        /// Used for internal removal of a registered handler (can be partial or full unregistration).
        /// </summary>
        private void UnregisterInternal(string full, FileSystemEventHandler? modified, ErrorEventHandler? error) {
            if (_monitors.TryGetValue(full, out ContentAwareFileMonitor? monitor)) {
                if (modified != null)
                    monitor.Modified -= modified;
                if (error != null)
                    monitor.Error -= error;

                // If there are no more subscribers, fully remove
                if (!monitor.HasSubscribers) {
                    monitor.Dispose();
                    _monitors.TryRemove(full, out _);
                }
            }
        }

        private void OnFsEvent(object? sender, FileSystemEventArgs e) {
            string full = Path.GetFullPath(e.FullPath);
            if (_monitors.TryGetValue(full, out ContentAwareFileMonitor? monitor)) {
                monitor.HandleExternalFsEvent(e);
            }
        }

        private void OnRenamed(object? sender, RenamedEventArgs e) {
            string newFull = Path.GetFullPath(e.FullPath);
            string oldFull = Path.GetFullPath(e.OldFullPath);

            // If a monitored file was renamed to a new monitored _fullPath, treat appropriately.
            if (_monitors.TryGetValue(newFull, out ContentAwareFileMonitor? newMonitor)) {
                newMonitor.HandleExternalFsEvent(new FileSystemEventArgs(WatcherChangeTypes.Renamed, Path.GetDirectoryName(newFull)!, Path.GetFileName(newFull)));
            }

            if (_monitors.TryGetValue(oldFull, out ContentAwareFileMonitor? oldMonitor)) {
                // If a monitored file was renamed away, also trigger for the new name if needed.
                // Some semantics could vary; we just alert on rename into.
                if (!string.Equals(oldFull, newFull, StringComparison.OrdinalIgnoreCase)) {
                    newMonitor?.HandleExternalFsEvent(new FileSystemEventArgs(WatcherChangeTypes.Renamed, Path.GetDirectoryName(newFull)!, Path.GetFileName(newFull)));
                }
            }
        }

        private sealed class Subscription(
            DirectoryWatchManager manager,
            ContentAwareFileMonitor monitor,
            string fullPath,
            FileSystemEventHandler modified,
            ErrorEventHandler? error) : IFileMonitorHandle
        {
            private bool _disposed;

            public void Dispose() {
                if (_disposed) return;
                _disposed = true;
                manager.UnregisterInternal(fullPath, modified, error);
            }

            public void InternalModify(Action modification) {
                monitor.WriteInternally(modification);
            }

            public async Task InternalModifyAsync(Func<Task> modification) {
                await monitor.WriteInternallyAsync(modification);
            }
        }

        public void Dispose() {
            if (_disposed) return;
            _disposed = true;

            foreach (KeyValuePair<string, ContentAwareFileMonitor> kv in _monitors) {
                kv.Value.Dispose();
            }
            _monitors.Clear();

            _watcher.Changed -= OnFsEvent;
            _watcher.Created -= OnFsEvent;
            _watcher.Renamed -= OnRenamed;
            _watcher.Deleted -= OnFsEvent;
            _watcher.Dispose();
        }
    }
}
