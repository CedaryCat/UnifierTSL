using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnifierTSL.Logging;

namespace UnifierTSL.FileSystem
{
    sealed class ContentAwareFileWatcher : IDisposable
    {
        private readonly string _filePath;
        private readonly FileSystemWatcher _watcher;
        private readonly object _sync = new();
        private string? _lastHash;
        private Timer? _debounceTimer;
        private readonly TimeSpan _debounceInterval = TimeSpan.FromMilliseconds(300);
        private bool _pendingHandle = false;

        private bool _disposed = false;

        public event FileSystemEventHandler? Modified;
        public event ErrorEventHandler? Error;

        public ContentAwareFileWatcher(string filePath) {
            _filePath = Path.GetFullPath(filePath);
            var dir = Path.GetDirectoryName(_filePath)!;
            var fileName = Path.GetFileName(_filePath);

            _watcher = new FileSystemWatcher(dir) {
                Filter = fileName,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime | NotifyFilters.FileName,
                EnableRaisingEvents = true,
                IncludeSubdirectories = false
            };

            _watcher.Changed += OnFsEvent;
            _watcher.Created += OnFsEvent;
            _watcher.Renamed += OnRenamed;
            _watcher.Error += OnError;

            // Initialize existing hash if file exists
            if (File.Exists(_filePath)) {
                _lastHash = TryComputeHashWithRetry();
            }
        }

        private void OnError(object sender, ErrorEventArgs e) {
            Error?.Invoke(sender, e);
        }

        private void OnFsEvent(object? sender, FileSystemEventArgs e) {
            if (!IsTargetPath(e.FullPath))
                return;

            ScheduleHandleContentChange("FsEvent");
        }

        private void OnRenamed(object? sender, RenamedEventArgs e) {
            // Only handle "renamed to target file name"
            if (IsTargetPath(e.FullPath) && !IsTargetPath(e.OldFullPath)) {
                ScheduleHandleContentChange("RenamedToTarget");
            }
        }

        private bool IsTargetPath(string path) {
            return string.Equals(Path.GetFullPath(path), _filePath, StringComparison.OrdinalIgnoreCase);
        }

        private void ScheduleHandleContentChange(string reason) {
            lock (_sync) {
                _pendingHandle = true;
                _debounceTimer?.Dispose();
                _debounceTimer = new Timer(async _ => await DebouncedHandler(), null, _debounceInterval, Timeout.InfiniteTimeSpan);
            }
        }

        private async Task DebouncedHandler() {
            lock (_sync) {
                if (!_pendingHandle) return;
                _pendingHandle = false;
            }

            string? newHash = TryComputeHashWithRetry();
            if (newHash == null) {
                await Task.Delay(200);
                newHash = TryComputeHashWithRetry();
            }

            if (newHash == null) {
                // Give up this round
                return;
            }

            lock (_sync) {
                if (_lastHash == newHash) {
                    return;
                }

                _lastHash = newHash;
            }

            // Real content change or new file
            HandleContentChanged();
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
                new FileSystemEventArgs(WatcherChangeTypes.Changed, Path.GetDirectoryName(_filePath)!, Path.GetFileName(_filePath)));
        }

        public void Dispose() {
            if (_disposed) return;
            _disposed = true;

            Error = null;
            Modified = null;
            _watcher.Changed -= OnFsEvent;
            _watcher.Created -= OnFsEvent;
            _watcher.Renamed -= OnRenamed;
            _watcher.Error -= OnError;
            _watcher.Dispose();
            _debounceTimer?.Dispose();
        }
    }
}
