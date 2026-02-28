using System.Runtime.CompilerServices;
using UnifierTSL.Logging.LogTrace;
using UnifierTSL.Logging.Metadata;

namespace UnifierTSL.Logging
{
    public static class LogFactory
    {
        public static void CreateLog(
            LogLevel level,
            string role,
            string category,
            string message,
            out LogEntry logEntry,
            int eventId = default,
            Exception? exception = null,
            [CallerFilePath] string? sourceFilePath = null,
            [CallerMemberName] string? memberName = null,
            [CallerLineNumber] int? sourceLineNumber = null) {

            logEntry = new LogEntry(
                timestampUtc: DateTimeOffset.UtcNow,
                level: level,
                eventId: eventId,
                role: role,
                message: message,
                category: category,
                exception: exception,
                sourceFilePath: sourceFilePath,
                memberName: memberName,
                sourceLineNumber: sourceLineNumber
            );
        }
        public static void CreateLog(
            LogLevel level,
            string role,
            string category,
            string message,
            in TraceContext traceContext,
            out LogEntry logEntry,
            int eventId = default,
            Exception? exception = null,
            [CallerFilePath] string? sourceFilePath = null,
            [CallerMemberName] string? memberName = null,
            [CallerLineNumber] int? sourceLineNumber = null) {

            logEntry = new LogEntry(
                timestampUtc: DateTimeOffset.UtcNow,
                level: level,
                eventId: eventId,
                role: role,
                message: message,
                category: category,
                traceContext: traceContext,
                exception: exception,
                sourceFilePath: sourceFilePath,
                memberName: memberName,
                sourceLineNumber: sourceLineNumber
            );
        }
        internal static void CreateLog(
            LogLevel level,
            string role,
            string category,
            string message,
            ref MetadataAllocHandle metadataAllocHandle,
            out LogEntry logEntry,
            int eventId = default,
            Exception? exception = null,
            [CallerFilePath] string? sourceFilePath = null,
            [CallerMemberName] string? memberName = null,
            [CallerLineNumber] int? sourceLineNumber = null) {

            logEntry = new LogEntry(
                timestampUtc: DateTimeOffset.UtcNow,
                level: level,
                eventId: eventId,
                role: role,
                category: category,
                message: message,
                exception: exception,
                sourceFilePath: sourceFilePath,
                memberName: memberName,
                sourceLineNumber: sourceLineNumber,
                ref metadataAllocHandle
            );
        }
        internal static void CreateLog(
            LogLevel level,
            string role,
            string category,
            string message,
            in TraceContext traceContext,
            ref MetadataAllocHandle metadataAllocHandle,
            out LogEntry logEntry,
            int eventId = default,
            Exception? exception = null,
            [CallerFilePath] string? sourceFilePath = null,
            [CallerMemberName] string? memberName = null,
            [CallerLineNumber] int? sourceLineNumber = null) {

            logEntry = new LogEntry(
                timestampUtc: DateTimeOffset.UtcNow,
                level: level,
                eventId: eventId,
                role: role,
                category: category,
                message: message,
                traceContext: traceContext,
                exception: exception,
                sourceFilePath: sourceFilePath,
                memberName: memberName,
                sourceLineNumber: sourceLineNumber,
                ref metadataAllocHandle
            );
        }
        public static void CreateException(
            string role,
            string category,
            string message,
            Exception exception,
            in TraceContext traceContext,
            out LogEntry logEntry,
            int eventId = default,
            [CallerFilePath] string? sourceFilePath = null,
            [CallerMemberName] string? memberName = null,
            [CallerLineNumber] int? sourceLineNumber = null) {

            logEntry = new LogEntry(
                timestampUtc: DateTimeOffset.UtcNow,
                level: LogLevel.Error,
                eventId: eventId,
                role: role,
                category: category,
                message: message,
                traceContext: traceContext,
                exception: exception,
                sourceFilePath: sourceFilePath,
                memberName: memberName,
                sourceLineNumber: sourceLineNumber
            );
        }
        public static void CreateException(
            string role,
            string category,
            string message,
            Exception exception,
            out LogEntry logEntry,
            int eventId = default,
            [CallerFilePath] string? sourceFilePath = null,
            [CallerMemberName] string? memberName = null,
            [CallerLineNumber] int? sourceLineNumber = null) {

            logEntry = new LogEntry(
                timestampUtc: DateTimeOffset.UtcNow,
                level: LogLevel.Error,
                eventId: eventId,
                role: role,
                category: category,
                message: message,
                exception: exception,
                sourceFilePath: sourceFilePath,
                memberName: memberName,
                sourceLineNumber: sourceLineNumber
            );
        }
        internal static void CreateException(
            string role,
            string category,
            string message,
            Exception exception,
            ref MetadataAllocHandle metadataAllocHandle,
            out LogEntry logEntry,
            int eventId = default,
            [CallerFilePath] string? sourceFilePath = null,
            [CallerMemberName] string? memberName = null,
            [CallerLineNumber] int? sourceLineNumber = null) {

            logEntry = new LogEntry(
                timestampUtc: DateTimeOffset.UtcNow,
                level: LogLevel.Error,
                eventId: eventId,
                role: role,
                category: category,
                message: message,
                exception: exception,
                sourceFilePath: sourceFilePath,
                memberName: memberName,
                sourceLineNumber: sourceLineNumber,
                ref metadataAllocHandle
            );
        }
        internal static void CreateException(
            string role,
            string category,
            string message,
            Exception exception,
            in TraceContext traceContext,
            ref MetadataAllocHandle metadataAllocHandle,
            out LogEntry logEntry,
            int eventId = default,
            [CallerFilePath] string? sourceFilePath = null,
            [CallerMemberName] string? memberName = null,
            [CallerLineNumber] int? sourceLineNumber = null) {

            logEntry = new LogEntry(
                timestampUtc: DateTimeOffset.UtcNow,
                level: LogLevel.Error,
                eventId: eventId,
                role: role,
                category: category,
                message: message,
                traceContext: traceContext,
                exception: exception,
                sourceFilePath: sourceFilePath,
                memberName: memberName,
                sourceLineNumber: sourceLineNumber,
                ref metadataAllocHandle
            );
        }
    }
}
