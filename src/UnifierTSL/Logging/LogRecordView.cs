using System.Diagnostics.CodeAnalysis;
using UnifierTSL.Logging.Metadata;

namespace UnifierTSL.Logging
{
    internal readonly ref struct LogRecordView
    {
        private readonly BufferedLogRecord record;
        private readonly ReadOnlySpan<KeyValueMetadata> metadata;

        internal LogRecordView(in BufferedLogRecord record, ReadOnlySpan<KeyValueMetadata> metadata) {
            this.record = record;
            this.metadata = metadata;
        }

        public ulong Sequence => record.Sequence;
        public DateTimeOffset TimestampUtc => record.TimestampUtc;
        public LogLevel Level => record.Level;
        public int EventId => record.EventId;
        public string Role => record.Role;
        public string Category => record.Category;
        public string Message => record.Message;
        public Exception? Exception => record.Exception;
        public string? SourceFilePath => record.SourceFilePath;
        public string? MemberName => record.MemberName;
        public int? SourceLineNumber => record.SourceLineNumber;
        public bool HasTraceContext => (record.Flags & BufferedLogRecordFlags.HasTraceContext) != 0;
        public bool MetadataOverflowed => (record.Flags & BufferedLogRecordFlags.MetadataOverflowed) != 0;
        public ReadOnlySpan<KeyValueMetadata> Metadata => metadata;

        public readonly LogTrace.TraceContext GetTraceContextOrDefault() => record.TraceContext;

        public readonly bool TryGetMetadata(ReadOnlySpan<char> key, [NotNullWhen(true)] out string? value) {
            ReadOnlySpan<KeyValueMetadata> entries = metadata;
            for (int i = 0; i < entries.Length; i++) {
                if (key.Equals(entries[i].Key, StringComparison.Ordinal)) {
                    value = entries[i].Value;
                    return true;
                }
            }

            value = null;
            return false;
        }

        public readonly string? GetMetadata(string key) => TryGetMetadata(key, out string? value) ? value : null;
    }
}
