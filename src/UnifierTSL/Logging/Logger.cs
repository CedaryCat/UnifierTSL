using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using UnifierTSL.Logging.LogFilters;
using UnifierTSL.Logging.LogWriters;
using UnifierTSL.Logging.Metadata;

namespace UnifierTSL.Logging
{
    public class Logger : IMetadataInjectionHost
    {
        public const int RecordCapacity = 1024;
        public const int MetadataArenaCapacity = 8192;

        private readonly Lock publishSync = new();
        private readonly BufferedLogRecord[] records = new BufferedLogRecord[RecordCapacity];
        private readonly KeyValueMetadata[] metadataArena = new KeyValueMetadata[MetadataArenaCapacity];

        private int recordStartIndex;
        private int recordCount;
        private int nextRecordIndex;
        private int metadataHeadIndex;
        private int metadataTailIndex;
        private int metadataUsed;
        private ulong nextSequence = 1;
        private bool historyEnabled = true;

        private ILogFilter filter = EmptyLogFilter.Instance;
        public ILogFilter? Filter {
            [return: NotNull]
            get => filter;
            set {
                filter = value ?? EmptyLogFilter.Instance;
            }
        }

        private ILogWriter writer = ConsoleLogWriter.Instance;
        public ILogWriter? Writer {
            [return: NotNull]
            get => writer;
            set {
                lock (publishSync) {
                    writer = value ?? ConsoleLogWriter.Instance;
                }
            }
        }

        public ulong LatestSequence {
            get {
                lock (publishSync) {
                    return nextSequence - 1;
                }
            }
        }

        internal bool HistoryEnabled {
            get {
                lock (publishSync) {
                    return historyEnabled;
                }
            }
            set {
                lock (publishSync) {
                    if (historyEnabled == value) {
                        return;
                    }

                    historyEnabled = value;
                    if (!value) {
                        ClearHistoryBuffers();
                    }
                }
            }
        }

        private ImmutableArray<ILogMetadataInjector> _injectors = ImmutableArray<ILogMetadataInjector>.Empty;
        public IReadOnlyList<ILogMetadataInjector> MetadataInjectors => _injectors;

        public void AddMetadataInjector(ILogMetadataInjector injector) {
            ArgumentNullException.ThrowIfNull(injector);
            ImmutableInterlocked.Update(ref _injectors, arr => arr.Contains(injector) ? arr : arr.Add(injector));
        }

        public void RemoveMetadataInjector(ILogMetadataInjector injector) {
            ArgumentNullException.ThrowIfNull(injector);
            ImmutableInterlocked.Update(ref _injectors, arr => arr.Remove(injector));
        }

        public void AddWriter(ILogWriter writer) {
            ArgumentNullException.ThrowIfNull(writer);
            lock (publishSync) {
                this.writer += writer;
            }
        }

        public void RemoveWriter(ILogWriter writer) {
            ArgumentNullException.ThrowIfNull(writer);
            lock (publishSync) {
                this.writer = this.writer - writer ?? ConsoleLogWriter.Instance;
            }
        }

        internal int ReplayHistory(ulong afterSequenceExclusive, int maxCount, ILogHistorySink sink) {
            ArgumentNullException.ThrowIfNull(sink);
            ArgumentOutOfRangeException.ThrowIfNegative(maxCount);

            lock (publishSync) {
                if (!historyEnabled) {
                    return 0;
                }

                return ReplayHistoryCore(afterSequenceExclusive, maxCount, sink);
            }
        }

        internal int AttachHistoryWriter(ILogWriter writer, ILogHistorySink historySink, ulong afterSequenceExclusive = 0, int maxCount = int.MaxValue) {
            ArgumentNullException.ThrowIfNull(writer);
            ArgumentNullException.ThrowIfNull(historySink);
            ArgumentOutOfRangeException.ThrowIfNegative(maxCount);

            lock (publishSync) {
                int replayed = historyEnabled ? ReplayHistoryCore(afterSequenceExclusive, maxCount, historySink) : 0;
                this.writer = this.writer + writer;
                return replayed;
            }
        }

        public void Log(ref LogEntry entry) {
            ReadOnlySpan<ILogMetadataInjector> injectors = _injectors.AsSpan();
            int injectorCount = injectors.Length;
            if (injectorCount > 0) {
                ref ILogMetadataInjector element0 = ref MemoryMarshal.GetReference(injectors);
                for (int i = 0; i < injectorCount; i++) {
                    Unsafe.Add(ref element0, i).InjectMetadata(ref entry);
                }
            }

            if (!filter.ShouldLog(in entry)) {
                return;
            }

            lock (publishSync) {
                if (historyEnabled) {
                    CommitToHistory(in entry);
                }

                writer.Write(in entry);
            }
        }

        private void ClearHistoryBuffers() {
            Array.Clear(records);
            Array.Clear(metadataArena);

            recordStartIndex = 0;
            recordCount = 0;
            nextRecordIndex = 0;
            metadataHeadIndex = 0;
            metadataTailIndex = 0;
            metadataUsed = 0;
        }

        private void CommitToHistory(scoped in LogEntry entry) {
            int metadataCount = entry.MetadataCount;
            EnsureSpaceForNextRecord(metadataCount);

            ulong sequence = nextSequence++;
            int metadataStart = 0;
            if (metadataCount > 0) {
                metadataStart = ReserveMetadataSpace(metadataCount);
                bool metadataWasEmpty = metadataUsed == 0;
                Span<KeyValueMetadata> metadataDestination = metadataArena.AsSpan(metadataStart, metadataCount);
                for (int i = 0; i < metadataCount; i++) {
                    metadataDestination[i] = entry.GetMetadataAt(i);
                }
                if (metadataWasEmpty) {
                    metadataTailIndex = metadataStart;
                }
                metadataHeadIndex = (metadataStart + metadataCount) % MetadataArenaCapacity;
                metadataUsed += metadataCount;
            }

            BufferedLogRecordFlags flags = BufferedLogRecordFlags.None;
            if (entry.HasTraceContext) {
                flags |= BufferedLogRecordFlags.HasTraceContext;
            }
            if (entry.MetadataOverflowed) {
                flags |= BufferedLogRecordFlags.MetadataOverflowed;
            }

            int recordIndex = nextRecordIndex;
            records[recordIndex] = new BufferedLogRecord(
                sequence: sequence,
                timestampUtc: entry.TimestampUtc,
                level: entry.Level,
                eventId: entry.EventId,
                role: entry.Role,
                category: entry.Category,
                message: entry.Message,
                exception: entry.Exception,
                sourceFilePath: entry.SourceFilePath,
                memberName: entry.MemberName,
                sourceLineNumber: entry.SourceLineNumber,
                traceContext: entry.GetTraceContextOrDefault(),
                flags: flags,
                metadataStart: metadataStart,
                metadataCount: (ushort)metadataCount);

            if (recordCount == 0) {
                recordStartIndex = recordIndex;
            }
            recordCount++;
            nextRecordIndex = (recordIndex + 1) % RecordCapacity;
        }

        private int ReplayHistoryCore(ulong afterSequenceExclusive, int maxCount, ILogHistorySink sink) {
            int emitted = 0;
            for (int i = 0; i < recordCount && emitted < maxCount; i++) {
                int index = (recordStartIndex + i) % RecordCapacity;
                ref readonly BufferedLogRecord record = ref records[index];
                if (record.Sequence <= afterSequenceExclusive) {
                    continue;
                }

                LogRecordView view = new(in record, GetMetadataSpan(in record));
                sink.Write(in view);
                emitted++;
            }

            return emitted;
        }

        private void EnsureSpaceForNextRecord(int metadataCount) {
            while (recordCount >= RecordCapacity) {
                EvictOldestRecord();
            }

            if (metadataCount <= 0) {
                return;
            }

            while (!CanReserveMetadata(metadataCount)) {
                EvictOldestRecord();
            }
        }

        private bool CanReserveMetadata(int metadataCount) {
            if (metadataCount > MetadataArenaCapacity) {
                return false;
            }

            if (metadataUsed == 0) {
                return true;
            }

            if (metadataCount > MetadataArenaCapacity - metadataUsed) {
                return false;
            }

            if (metadataHeadIndex < metadataTailIndex) {
                return metadataCount <= metadataTailIndex - metadataHeadIndex;
            }

            int tailSpace = MetadataArenaCapacity - metadataHeadIndex;
            if (metadataCount <= tailSpace) {
                return true;
            }

            return metadataCount <= metadataTailIndex;
        }

        private int ReserveMetadataSpace(int metadataCount) {
            if (metadataCount <= 0 || metadataUsed == 0) {
                metadataHeadIndex = 0;
                return 0;
            }

            if (metadataHeadIndex < metadataTailIndex) {
                return metadataHeadIndex;
            }

            int tailSpace = MetadataArenaCapacity - metadataHeadIndex;
            if (metadataCount <= tailSpace) {
                return metadataHeadIndex;
            }

            metadataHeadIndex = 0;
            return 0;
        }

        private void EvictOldestRecord() {
            if (recordCount <= 0) {
                return;
            }

            ref readonly BufferedLogRecord record = ref records[recordStartIndex];
            if (record.MetadataCount > 0) {
                metadataTailIndex = (record.MetadataStart + record.MetadataCount) % MetadataArenaCapacity;
                metadataUsed -= record.MetadataCount;
                if (metadataUsed < 0) {
                    metadataUsed = 0;
                }
                if (metadataUsed == 0) {
                    metadataTailIndex = metadataHeadIndex;
                }
            }

            recordStartIndex = (recordStartIndex + 1) % RecordCapacity;
            recordCount--;
        }

        private ReadOnlySpan<KeyValueMetadata> GetMetadataSpan(scoped in BufferedLogRecord record) {
            if (record.MetadataCount == 0) {
                return [];
            }

            return metadataArena.AsSpan(record.MetadataStart, record.MetadataCount);
        }
    }
}
