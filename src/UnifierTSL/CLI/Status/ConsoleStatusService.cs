using UnifierTSL.Servers;

namespace UnifierTSL.CLI.Status
{
    internal sealed class ConsoleStatusService(ServerContext? server)
    {
        public const int RefreshIntervalMs = 250;

        private sealed class OverlayEntry
        {
            public required long Id { get; init; }
            public required ConsoleStatusFrame Frame;
        }

        private readonly Lock sync = new();
        private readonly List<Func<ConsoleStatusResolveContext, ConsoleStatusFrame?>> baselines = [];
        private readonly List<OverlayEntry> overlays = [];
        private string lastSignature = "<unset>";
        private long nextOverlayId;
        private bool disposed;

        public void RegisterBaseline(Func<ConsoleStatusResolveContext, ConsoleStatusFrame?> provider) {
            ArgumentNullException.ThrowIfNull(provider);
            lock (sync) {
                ThrowIfDisposed();
                baselines.Add(provider);
            }
        }

        public OverlayScope PushOverlayScope(ConsoleStatusFrame initialFrame) {
            lock (sync) {
                ThrowIfDisposed();
                OverlayEntry entry = new() {
                    Id = nextOverlayId++,
                    Frame = initialFrame,
                };
                overlays.Add(entry);
                return new OverlayScope(this, entry.Id);
            }
        }

        public bool TryPeekTopFrame(out ConsoleStatusFrame frame) {
            ThrowIfDisposed();
            if (TryPeekOverlayFrame(out frame)) {
                return true;
            }

            Func<ConsoleStatusResolveContext, ConsoleStatusFrame?>[] providers;
            lock (sync) {
                providers = [.. baselines];
            }

            if (providers.Length == 0) {
                frame = default;
                return false;
            }

            // Baseline providers may execute plugin/runtime callbacks and can themselves emit log
            // or status traffic. Snapshot the registry under sync, then invoke providers after the
            // lock is released; holding sync across callbacks would serialize the whole pipeline and
            // make reentrancy deadlocks trivial.
            ConsoleStatusResolveContext context = new(
                Server: server,
                SampleUtc: DateTimeOffset.UtcNow);
            foreach (Func<ConsoleStatusResolveContext, ConsoleStatusFrame?> provider in providers) {
                try {
                    ConsoleStatusFrame? candidate = provider(context);
                    if (candidate.HasValue) {
                        frame = candidate.Value;
                        return true;
                    }
                }
                catch {
                }
            }

            frame = default;
            return false;
        }

        public bool TryGetTopFrameIfChanged(out ConsoleStatusFrame frame, out bool hasFrame) {
            hasFrame = TryPeekTopFrame(out frame);
            string signature = ConsoleStatusFrameSignature.Build(hasFrame, frame);

            lock (sync) {
                if (string.Equals(signature, lastSignature, StringComparison.Ordinal)) {
                    return false;
                }

                lastSignature = signature;
                return true;
            }
        }

        public void ResetChangeTracking() {
            lock (sync) {
                lastSignature = "<unset>";
            }
        }

        public void Dispose() {
            lock (sync) {
                if (disposed) {
                    return;
                }

                baselines.Clear();
                overlays.Clear();
                disposed = true;
            }
        }

        private bool TryPeekOverlayFrame(out ConsoleStatusFrame frame) {
            lock (sync) {
                if (overlays.Count == 0) {
                    frame = default;
                    return false;
                }

                frame = overlays[^1].Frame;
                return true;
            }
        }

        private void UpdateOverlay(long id, ConsoleStatusFrame frame) {
            lock (sync) {
                if (disposed) {
                    return;
                }

                for (int index = overlays.Count - 1; index >= 0; index--) {
                    if (overlays[index].Id == id) {
                        overlays[index].Frame = frame;
                        return;
                    }
                }
            }
        }

        private void RemoveOverlay(long id) {
            lock (sync) {
                if (disposed) {
                    return;
                }

                for (int index = overlays.Count - 1; index >= 0; index--) {
                    if (overlays[index].Id == id) {
                        overlays.RemoveAt(index);
                        return;
                    }
                }
            }
        }

        private void ThrowIfDisposed() {
            ObjectDisposedException.ThrowIf(disposed, this);
        }

        internal sealed class OverlayScope : IDisposable
        {
            private readonly ConsoleStatusService owner;
            private readonly long id;
            private int disposed;

            public OverlayScope(ConsoleStatusService owner, long id) {
                this.owner = owner;
                this.id = id;
            }

            public void Update(ConsoleStatusFrame frame) {
                if (Volatile.Read(ref disposed) != 0) {
                    return;
                }

                owner.UpdateOverlay(id, frame);
            }

            public void Dispose() {
                if (Interlocked.Exchange(ref disposed, 1) != 0) {
                    return;
                }

                owner.RemoveOverlay(id);
            }
        }
    }
}
