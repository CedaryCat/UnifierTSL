using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
    public readonly ref partial struct LogEntry
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

        public string? TraceId { get; init; }
        public string? SpanId { get; init; }
        public string? CorrelationId { get; init; }

        private readonly MetadataCollection metadata;
    }
    public readonly ref partial struct LogEntry
    {
        public readonly ReadOnlySpan<KeyValueMetadata> Metadata => metadata.Metadata;
        public readonly void SetMetadata(string key, string value) {
            metadata.Set(key, value);
        }
        public readonly string? GetMetadata(string key) {
            if (metadata.TryGet(key, out var value)) {
                return value;
            }
            return null;
        }
        public readonly override string ToString() {
            return $"[{Level}][{Role}]{Message}";
        }

        internal LogEntry(
            DateTimeOffset timestampUtc,
            LogLevel level,
            int eventId,
            string role,
            string message,
            string category,
            Exception? exception,
            string? sourceFilePath,
            string? memberName,
            int? sourceLineNumber,
            string? traceId,
            string? spanId,
            string? correlationId,
            ref MetadataAllocHandle metadataAllocHandle) {

            TimestampUtc = timestampUtc;
            Level = level;
            EventId = eventId;
            Role = role;
            Message = message;
            Category = category;
            Exception = exception;

            SourceFilePath = sourceFilePath;
            MemberName = memberName;
            SourceLineNumber = sourceLineNumber;

            TraceId = traceId;
            SpanId = spanId;
            CorrelationId = correlationId;

            metadata = new(ref metadataAllocHandle);
        }

        /// <summary>
        /// Do not support metadata
        /// </summary>
        /// <param name="timestampUtc"></param>
        /// <param name="level"></param>
        /// <param name="eventId"></param>
        /// <param name="role"></param>
        /// <param name="message"></param>
        /// <param name="category"></param>
        /// <param name="exception"></param>
        /// <param name="sourceFilePath"></param>
        /// <param name="memberName"></param>
        /// <param name="sourceLineNumber"></param>
        /// <param name="traceId"></param>
        /// <param name="spanId"></param>
        /// <param name="correlationId"></param>
        public LogEntry(
            DateTimeOffset timestampUtc,
            LogLevel level,
            int eventId,
            string role,
            string message,
            string category,
            Exception? exception = null,
            string? sourceFilePath = null,
            string? memberName = null,
            int? sourceLineNumber = null,
            string? traceId = null,
            string? spanId = null,
            string? correlationId = null) {

            TimestampUtc = timestampUtc;
            Level = level;
            EventId = eventId;
            Role = role;
            Message = message;
            Category = category;
            Exception = exception;

            SourceFilePath = sourceFilePath;
            MemberName = memberName;
            SourceLineNumber = sourceLineNumber;

            TraceId = traceId;
            SpanId = spanId;
            CorrelationId = correlationId;

            metadata = default;
        }
    }
}
