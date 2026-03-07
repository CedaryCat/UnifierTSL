using System.Collections.Immutable;
using System.Threading;
using UnifierTSL.Logging.LogFilters;
using UnifierTSL.Logging.LogWriters;

namespace UnifierTSL.Logging
{
    internal sealed record LoggerPipelineSnapshot(
        ImmutableArray<ILogMetadataInjector> MetadataInjectors,
        ILogFilter Filter,
        ILogWriter Writer)
    {
        public static readonly LoggerPipelineSnapshot Empty = new(
            ImmutableArray<ILogMetadataInjector>.Empty,
            EmptyLogFilter.Instance,
            EmptyLogWriter.Instance);
    }

    internal sealed class LoggerPipeline : IMetadataInjectionHost
    {
        private LoggerPipelineSnapshot snapshot = LoggerPipelineSnapshot.Empty;

        public LoggerPipelineSnapshot Snapshot => Volatile.Read(ref snapshot);

        public ILogFilter? Filter {
            get => Snapshot.Filter;
            set {
                ILogFilter resolved = value ?? EmptyLogFilter.Instance;
                UpdateSnapshot(current => ReferenceEquals(current.Filter, resolved)
                    ? current
                    : current with { Filter = resolved });
            }
        }

        public ILogWriter? Writer {
            get => Snapshot.Writer;
            set {
                ILogWriter resolved = value ?? EmptyLogWriter.Instance;
                UpdateSnapshot(current => ReferenceEquals(current.Writer, resolved)
                    ? current
                    : current with { Writer = resolved });
            }
        }

        public IReadOnlyList<ILogMetadataInjector> MetadataInjectors => Snapshot.MetadataInjectors;

        public void AddMetadataInjector(ILogMetadataInjector injector) {
            ArgumentNullException.ThrowIfNull(injector);
            UpdateSnapshot(current => current.MetadataInjectors.Contains(injector)
                ? current
                : current with { MetadataInjectors = current.MetadataInjectors.Add(injector) });
        }

        public void RemoveMetadataInjector(ILogMetadataInjector injector) {
            ArgumentNullException.ThrowIfNull(injector);
            UpdateSnapshot(current => current with { MetadataInjectors = current.MetadataInjectors.Remove(injector) });
        }

        public void AddWriter(ILogWriter writer) {
            ArgumentNullException.ThrowIfNull(writer);
            UpdateSnapshot(current => current with {
                Writer = current.Writer is EmptyLogWriter ? writer : current.Writer + writer
            });
        }

        public void RemoveWriter(ILogWriter writer) {
            ArgumentNullException.ThrowIfNull(writer);
            UpdateSnapshot(current => current with {
                Writer = current.Writer is EmptyLogWriter
                    ? current.Writer
                    : current.Writer - writer ?? EmptyLogWriter.Instance
            });
        }

        private void UpdateSnapshot(Func<LoggerPipelineSnapshot, LoggerPipelineSnapshot> updater) {
            ArgumentNullException.ThrowIfNull(updater);

            while (true) {
                LoggerPipelineSnapshot current = Snapshot;
                LoggerPipelineSnapshot next = updater(current);
                if (ReferenceEquals(current, next)) {
                    return;
                }

                if (ReferenceEquals(Interlocked.CompareExchange(ref snapshot, next, current), current)) {
                    return;
                }
            }
        }
    }
}
