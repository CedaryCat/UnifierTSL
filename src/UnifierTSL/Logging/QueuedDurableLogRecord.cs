using System.Buffers;
using UnifierTSL.Logging.LogTrace;
using UnifierTSL.Logging.Metadata;

namespace UnifierTSL.Logging
{
    internal struct QueuedDurableLogRecord
    {
        private KeyValueMetadata[]? metadataBuffer;

        public DateTimeOffset TimestampUtc;
        public LogLevel Level;
        public int EventId;
        public string Role;
        public string Category;
        public string? ServerContext;
        public string Message;
        public Exception? Exception;
        public string? SourceFilePath;
        public string? MemberName;
        public int? SourceLineNumber;
        public TraceContext TraceContext;
        public bool HasTraceContext;
        public bool MetadataOverflowed;
        public int MetadataCount;

        public readonly ReadOnlySpan<KeyValueMetadata> Metadata {
            get {
                if (metadataBuffer is null || MetadataCount <= 0) {
                    return [];
                }

                return metadataBuffer.AsSpan(0, MetadataCount);
            }
        }

        public static QueuedDurableLogRecord FromLogEntry(scoped in LogEntry entry) {
            QueuedDurableLogRecord record = new() {
                TimestampUtc = entry.TimestampUtc,
                Level = entry.Level,
                EventId = entry.EventId,
                Role = entry.Role,
                Category = entry.Category,
                ServerContext = entry.GetMetadata("ServerContext"),
                Message = entry.Message,
                Exception = entry.Exception,
                SourceFilePath = entry.SourceFilePath,
                MemberName = entry.MemberName,
                SourceLineNumber = entry.SourceLineNumber,
                TraceContext = entry.GetTraceContextOrDefault(),
                HasTraceContext = entry.HasTraceContext,
                MetadataOverflowed = entry.MetadataOverflowed,
                MetadataCount = entry.MetadataCount,
            };

            if (record.MetadataCount > 0) {
                KeyValueMetadata[] buffer = ArrayPool<KeyValueMetadata>.Shared.Rent(record.MetadataCount);
                for (int i = 0; i < record.MetadataCount; i++) {
                    buffer[i] = entry.GetMetadataAt(i);
                }

                record.metadataBuffer = buffer;
            }

            return record;
        }

        public static QueuedDurableLogRecord FromLogRecordView(scoped in LogRecordView recordView) {
            ReadOnlySpan<KeyValueMetadata> metadata = recordView.Metadata;
            int metadataCount = metadata.Length;

            QueuedDurableLogRecord record = new() {
                TimestampUtc = recordView.TimestampUtc,
                Level = recordView.Level,
                EventId = recordView.EventId,
                Role = recordView.Role,
                Category = recordView.Category,
                ServerContext = recordView.GetMetadata("ServerContext"),
                Message = recordView.Message,
                Exception = recordView.Exception,
                SourceFilePath = recordView.SourceFilePath,
                MemberName = recordView.MemberName,
                SourceLineNumber = recordView.SourceLineNumber,
                TraceContext = recordView.GetTraceContextOrDefault(),
                HasTraceContext = recordView.HasTraceContext,
                MetadataOverflowed = recordView.MetadataOverflowed,
                MetadataCount = metadataCount,
            };

            if (metadataCount > 0) {
                KeyValueMetadata[] buffer = ArrayPool<KeyValueMetadata>.Shared.Rent(metadataCount);
                metadata.CopyTo(buffer.AsSpan(0, metadataCount));
                record.metadataBuffer = buffer;
            }

            return record;
        }

        public void ReturnPooledResources() {
            KeyValueMetadata[]? buffer = metadataBuffer;
            metadataBuffer = null;
            MetadataCount = 0;

            if (buffer is not null) {
                ArrayPool<KeyValueMetadata>.Shared.Return(buffer);
            }
        }
    }
}
