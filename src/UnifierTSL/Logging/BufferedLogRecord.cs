using UnifierTSL.Logging.LogTrace;

namespace UnifierTSL.Logging
{
    [Flags]
    internal enum BufferedLogRecordFlags : byte
    {
        None = 0,
        HasTraceContext = 1 << 0,
        MetadataOverflowed = 1 << 1,
    }

    internal readonly struct BufferedLogRecord
    {
        public readonly ulong Sequence;
        public readonly DateTimeOffset TimestampUtc;
        public readonly LogLevel Level;
        public readonly int EventId;
        public readonly string Role;
        public readonly string Category;
        public readonly string Message;
        public readonly Exception? Exception;
        public readonly string? SourceFilePath;
        public readonly string? MemberName;
        public readonly int? SourceLineNumber;
        public readonly TraceContext TraceContext;
        public readonly BufferedLogRecordFlags Flags;
        public readonly int MetadataStart;
        public readonly ushort MetadataCount;

        public BufferedLogRecord(
            ulong sequence,
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
            in TraceContext traceContext,
            BufferedLogRecordFlags flags,
            int metadataStart,
            ushort metadataCount) {

            Sequence = sequence;
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
            TraceContext = traceContext;
            Flags = flags;
            MetadataStart = metadataStart;
            MetadataCount = metadataCount;
        }
    }
}
