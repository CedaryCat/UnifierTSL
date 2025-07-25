using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
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
            string? traceId = null,
            string? spanId = null,
            string? correlationId = null,
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
                sourceLineNumber: sourceLineNumber,
                traceId: traceId,
                spanId: spanId,
                correlationId: correlationId
            );
        }
        public static void CreateLog(
            LogLevel level,
            string role,
            string category,
            string message,
            ref MetadataAllocHandle metadataAllocHandle,
            out LogEntry logEntry,
            int eventId = default,
            Exception? exception = null,
            string? traceId = null,
            string? spanId = null,
            string? correlationId = null,
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
                sourceLineNumber: sourceLineNumber,
                traceId: traceId,
                spanId: spanId,
                correlationId: correlationId,
                ref metadataAllocHandle
            );
        }
        public static void CreateException(
            string role,
            string category,
            string message,
            Exception exception,
            out LogEntry logEntry,
            int eventId = default,
            string? traceId = null,
            string? spanId = null,
            string? correlationId = null,
            [CallerFilePath] string? sourceFilePath = null,
            [CallerMemberName] string? memberName = null,
            [CallerLineNumber] int? sourceLineNumber = null) {

            logEntry = new LogEntry(
                timestampUtc: DateTimeOffset.UtcNow,
                level: LogLevel.Error,
                eventId: eventId,
                role: role,
                message: message,
                category: category,
                exception: exception,
                sourceFilePath: sourceFilePath,
                memberName: memberName,
                sourceLineNumber: sourceLineNumber,
                traceId: traceId,
                spanId: spanId,
                correlationId: correlationId
            );
        }
        public static void CreateException(
            string role,
            string category,
            string message,
            Exception exception,
            ref MetadataAllocHandle metadataAllocHandle,
            out LogEntry logEntry,
            int eventId = default,
            string? traceId = null,
            string? spanId = null,
            string? correlationId = null,
            [CallerFilePath] string? sourceFilePath = null,
            [CallerMemberName] string? memberName = null,
            [CallerLineNumber] int? sourceLineNumber = null) {

            logEntry = new LogEntry(
                timestampUtc: DateTimeOffset.UtcNow,
                level: LogLevel.Error,
                eventId: eventId,
                role: role,
                message: message,
                category: category,
                exception: exception,
                sourceFilePath: sourceFilePath,
                memberName: memberName,
                sourceLineNumber: sourceLineNumber,
                traceId: traceId,
                spanId: spanId,
                correlationId: correlationId,
                ref metadataAllocHandle
            );
        }
    }
}
