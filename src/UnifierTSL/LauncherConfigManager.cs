using UnifierTSL.FileSystem;

namespace UnifierTSL
{
    internal sealed class LauncherConfigManager(LauncherConfigStore store, Action reloadAction) : IDisposable
    {
        private readonly Lock monitorGate = new();
        private IFileMonitorHandle? monitor;
        private int reloadQueued;
        private int reloadLoopRunning;
        private int disposed;

        public void StartMonitoring() {
            lock (monitorGate) {
                if (Volatile.Read(ref disposed) != 0) {
                    return;
                }

                if (monitor is not null) {
                    return;
                }

                monitor = UnifierApi.FileMonitor.Register(store.RootConfigRelativePath, OnFileChanged, OnError);
            }
        }

        private void OnError(object sender, ErrorEventArgs e) {
            if (Volatile.Read(ref disposed) != 0) {
                return;
            }

            UnifierApi.Logger.Warning(
                GetParticularString("{0} is root config path", $"File watcher error occurred for root config '{store.RootConfigPath}'."),
                category: "Config",
                ex: e.GetException());
        }

        private void OnFileChanged(object sender, FileSystemEventArgs e) {
            if (Volatile.Read(ref disposed) != 0) {
                return;
            }

            Interlocked.Exchange(ref reloadQueued, 1);
            TryStartReloadLoop();
        }

        private void TryStartReloadLoop() {
            if (Interlocked.CompareExchange(ref reloadLoopRunning, 1, 0) != 0) {
                return;
            }

            _ = Task.Run(RunReloadLoop);
        }

        private void RunReloadLoop() {
            try {
                while (Volatile.Read(ref disposed) == 0 && Interlocked.Exchange(ref reloadQueued, 0) != 0) {
                    try {
                        reloadAction();
                    }
                    catch (Exception ex) {
                        UnifierApi.Logger.Error(
                            GetParticularString("{0} is root config path", $"An unexpected error occurred while reloading root config '{store.RootConfigPath}'. Keeping the current runtime settings."),
                            category: "Config",
                            ex: ex);
                    }
                }
            }
            finally {
                Volatile.Write(ref reloadLoopRunning, 0);

                if (Volatile.Read(ref disposed) == 0 && Volatile.Read(ref reloadQueued) != 0) {
                    TryStartReloadLoop();
                }
            }
        }

        public void Dispose() {
            Volatile.Write(ref disposed, 1);

            lock (monitorGate) {
                IFileMonitorHandle? current = monitor;
                monitor = null;

                try {
                    current?.Dispose();
                }
                catch {
                }
            }
        }
    }
}
