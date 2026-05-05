using System.Collections.Immutable;
using System.Threading;
using UnifierTSL.Logging.LogFilters;
using UnifierTSL.Logging.LogWriters;

namespace UnifierTSL.Logging
{
    internal sealed class LoggerPipelineSnapshot
    {
        public LoggerPipelineSnapshot(
            ImmutableArray<ILogMetadataInjector> metadataInjectors,
            ILogFilter filter,
            ILogWriter writer)
        {
            MetadataInjectors = metadataInjectors;
            Filter = filter;
            Writer = writer;
        }

        public ImmutableArray<ILogMetadataInjector> MetadataInjectors { get; }

        public ILogFilter Filter { get; }

        public ILogWriter Writer { get; }

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
                    : new LoggerPipelineSnapshot(current.MetadataInjectors, resolved, current.Writer));
            }
        }

        public ILogWriter? Writer {
            get => Snapshot.Writer;
            set {
                ILogWriter resolved = value ?? EmptyLogWriter.Instance;
                UpdateSnapshot(current => ReferenceEquals(current.Writer, resolved)
                    ? current
                    : new LoggerPipelineSnapshot(current.MetadataInjectors, current.Filter, resolved));
            }
        }

        public IReadOnlyList<ILogMetadataInjector> MetadataInjectors => Snapshot.MetadataInjectors;

        public void AddMetadataInjector(ILogMetadataInjector injector) {
            UpdateSnapshot(current => current.MetadataInjectors.Contains(injector)
                ? current
                : new LoggerPipelineSnapshot(current.MetadataInjectors.Add(injector), current.Filter, current.Writer));
        }

        public void RemoveMetadataInjector(ILogMetadataInjector injector) {
            UpdateSnapshot(current => new LoggerPipelineSnapshot(
                current.MetadataInjectors.Remove(injector),
                current.Filter,
                current.Writer));
        }

        public void AddWriter(ILogWriter writer) {
            UpdateSnapshot(current => new LoggerPipelineSnapshot(
                current.MetadataInjectors,
                current.Filter,
                current.Writer is EmptyLogWriter ? writer : current.Writer + writer));
        }

        public void RemoveWriter(ILogWriter writer) {
            UpdateSnapshot(current => new LoggerPipelineSnapshot(
                current.MetadataInjectors,
                current.Filter,
                current.Writer is EmptyLogWriter
                    ? current.Writer
                    : current.Writer - writer ?? EmptyLogWriter.Instance));
        }

        private void UpdateSnapshot(Func<LoggerPipelineSnapshot, LoggerPipelineSnapshot> updater) {

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
