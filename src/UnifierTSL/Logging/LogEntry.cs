using System.Runtime.CompilerServices;
using UnifierTSL.Logging.LogTrace;
using UnifierTSL.Logging.Metadata;

namespace UnifierTSL.Logging
{
    public enum LogLevel
    {
        Trace = 0,
        Debug = 1,
        Info = 2,
        Success = 3,
        Warning = 4,
        Error = 5,
        Critical = 6
    }

    public ref partial struct LogEntry
    {
        public DateTimeOffset TimestampUtc { get; init; }
        public LogLevel Level { get; init; }
        public int EventId { get; init; }
        public string Role { get; init; }
        public string Message { get; init; }
        public string Category { get; init; }
        public Exception? Exception { get; init; }
        public string? SourceFilePath { get; init; }
        public string? MemberName { get; init; }
        public int? SourceLineNumber { get; init; }
        public readonly ref readonly TraceContext TraceContext;

        private MetadataCollection metadata;
    }

    public ref partial struct LogEntry
    {
        public readonly bool HasTraceContext => !Unsafe.IsNullRef(in TraceContext);
        public readonly int MetadataCount => metadata.Count;
        public readonly bool MetadataOverflowed => metadata.Overflowed;

        public void SetMetadata(string key, string value) {
            ref MetadataCollection metadata = ref this.metadata;
            metadata.Set(key, value);
        }

        public readonly string? GetMetadata(string key) {
            if (metadata.TryGet(key, out string? value)) {
                return value;
            }
            return null;
        }

        public readonly KeyValueMetadata GetMetadataAt(int index) => metadata.GetAt(index);

        public readonly TraceContext GetTraceContextOrDefault() => HasTraceContext ? TraceContext : default;

        internal void ReleaseMetadataResources() {
            metadata.Dispose();
        }

        public override readonly string ToString() {
            return $"[{Level}][{Role}]{Message}";
        }

        internal LogEntry(
            DateTimeOffset timestampUtc,
            LogLevel level,
            int eventId,
            string role,
            string category,
            string message,
            in TraceContext traceContext,
            Exception? exception,
            string? sourceFilePath,
            string? memberName,
            int? sourceLineNumber,
            bool supportsMetadata) {

            TimestampUtc = timestampUtc;
            Level = level;
            EventId = eventId;
            Role = role;
            Category = category;
            Message = message;
            Exception = exception;
            TraceContext = ref traceContext;
            SourceFilePath = sourceFilePath;
            MemberName = memberName;
            SourceLineNumber = sourceLineNumber;
            metadata = new(supported: supportsMetadata);
        }

        internal LogEntry(
            DateTimeOffset timestampUtc,
            LogLevel level,
            int eventId,
            string role,
            string category,
            string message,
            Exception? exception,
            string? sourceFilePath,
            string? memberName,
            int? sourceLineNumber,
            bool supportsMetadata) {

            TimestampUtc = timestampUtc;
            Level = level;
            EventId = eventId;
            Role = role;
            Category = category;
            Message = message;
            Exception = exception;
            SourceFilePath = sourceFilePath;
            MemberName = memberName;
            SourceLineNumber = sourceLineNumber;
            metadata = new(supported: supportsMetadata);
        }

        /// <summary>
        /// Do not support metadata
        /// </summary>
        public LogEntry(
            DateTimeOffset timestampUtc,
            LogLevel level,
            int eventId,
            string role,
            string category,
            string message,
            Exception? exception,
            string? sourceFilePath,
            string? memberName,
            int? sourceLineNumber) {

            TimestampUtc = timestampUtc;
            Level = level;
            EventId = eventId;
            Role = role;
            Category = category;
            Message = message;
            Exception = exception;
            SourceFilePath = sourceFilePath;
            MemberName = memberName;
            SourceLineNumber = sourceLineNumber;
            metadata = new(supported: false);
        }

        /// <summary>
        /// Do not support metadata
        /// </summary>
        public LogEntry(
            DateTimeOffset timestampUtc,
            LogLevel level,
            int eventId,
            string role,
            string category,
            string message,
            in TraceContext traceContext,
            Exception? exception,
            string? sourceFilePath,
            string? memberName,
            int? sourceLineNumber) {

            TimestampUtc = timestampUtc;
            Level = level;
            EventId = eventId;
            Role = role;
            Category = category;
            Message = message;
            Exception = exception;
            TraceContext = ref traceContext;
            SourceFilePath = sourceFilePath;
            MemberName = memberName;
            SourceLineNumber = sourceLineNumber;
            metadata = new(supported: false);
        }
    }
}
