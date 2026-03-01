using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using UnifierTSL.Logging.Metadata;

namespace UnifierTSL.Logging.LogTrace
{
    [StructLayout(LayoutKind.Explicit)]
    public readonly struct CorrelationLogger : IStandardLogger
    {
        [FieldOffset(00)] public readonly Guid CorrelationId;
        [FieldOffset(16)] public readonly TraceId TraceId;
        [FieldOffset(32)] public readonly SpanId SpanId;
        [FieldOffset(00)] public readonly TraceContext Context;
        [FieldOffset(40)] public readonly RoleLogger RoleLogger;

        /// <summary>
        /// Initializes a new instance of the <see cref="CorrelationLogger"/> with the specified correlation ID and role logger.
        /// Useful for continuing an existing trace with a new span.
        /// </summary>
        /// <param name="correlationId">The correlation ID to continue with.</param>
        /// <param name="traceId">The trace ID to continue with.</param>
        /// <param name="spanId">The span ID for this specific operation.</param>
        /// <param name="roleLogger">The role logger to use for emitting logs.</param>
        public CorrelationLogger(Guid correlationId, TraceId traceId, SpanId spanId, RoleLogger roleLogger) {
            CorrelationId = correlationId;
            TraceId = traceId;
            SpanId = spanId;
            RoleLogger = roleLogger;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CorrelationLogger"/> with a given correlation ID.
        /// Automatically generates new trace and span identifiers.
        /// </summary>
        /// <param name="correlationId">The correlation ID to use.</param>
        /// <param name="roleLogger">The role logger to use for emitting logs.</param>
        public CorrelationLogger(in Guid correlationId, RoleLogger roleLogger) {
            CorrelationId = correlationId;
            TraceId.CreateRandom(out TraceId);
            SpanId.CreateRandom(out SpanId);
            RoleLogger = roleLogger;
        }

        /// <summary>
        /// Initializes a new root <see cref="CorrelationLogger"/> with a new correlation ID, trace ID, and span ID.
        /// </summary>
        /// <param name="roleLogger">The role logger to use for emitting logs.</param>
        public CorrelationLogger(RoleLogger roleLogger, TraceId traceId) {
            CorrelationId = Guid.NewGuid();
            TraceId = traceId;
            SpanId = SpanId.CreateRandom();
            RoleLogger = roleLogger;
        }

        /// <summary>
        /// Initializes a new root <see cref="CorrelationLogger"/> with a new correlation ID, trace ID, and span ID.
        /// </summary>
        /// <param name="roleLogger">The role logger to use for emitting logs.</param>
        public CorrelationLogger(RoleLogger roleLogger) {
            CorrelationId = Guid.NewGuid();
            TraceId = TraceId.CreateRandom();
            SpanId = SpanId.CreateRandom();
            RoleLogger = roleLogger;
        }

        /// <summary>
        /// Creates a new <see cref="CorrelationLogger"/> that continues the same trace with a new child span.
        /// </summary>
        /// <param name="newLogger">A new <see cref="CorrelationLogger"/> with the same correlation and trace ID, but a new span ID.</param>
        public void StartChildSpan(out CorrelationLogger newLogger) {
            newLogger = new(CorrelationId, TraceId, SpanId.CreateRandom(), RoleLogger);
        }

        /// <summary>
        /// Creates a copy of the current <see cref="CorrelationLogger"/> with the specified span ID.
        /// </summary>
        /// <param name="newSpanId">The new span ID to use.</param>
        /// <param name="newLogger">The new <see cref="CorrelationLogger"/> instance with the specified span ID.</param>
        public void WithSpanId(SpanId newSpanId, out CorrelationLogger newLogger) {
            newLogger = new(CorrelationId, TraceId, newSpanId, RoleLogger);
        }

        /// <summary>
        /// Creates a copy of the current <see cref="CorrelationLogger"/> with the specified trace ID.
        /// </summary>
        /// <param name="newTraceId">The new trace ID to use.</param>
        /// <param name="newLogger">The new <see cref="CorrelationLogger"/> instance with the specified trace ID.</param>
        public CorrelationLogger WithTraceId(TraceId newTraceId, out CorrelationLogger newLogger) {
            newLogger = new(CorrelationId, newTraceId, SpanId, RoleLogger);
            return newLogger;
        }

        /// <summary>
        /// Emits a log message with the provided level, message, and current correlation context information.
        /// </summary>
        /// <param name="level">The severity level of the log message.</param>
        /// <param name="message">The log message text.</param>
        /// <param name="overwriteCategory">An optional override for the default category.</param>
        /// <param name="exception">An optional exception to include in the log.</param>
        /// <param name="eventId">An optional event identifier.</param>
        /// <param name="sourceFilePath">The path of the source file that emitted the log (auto-filled).</param>
        /// <param name="memberName">The name of the member emitting the log (auto-filled).</param>
        /// <param name="sourceLineNumber">The source line number where the log was emitted (auto-filled).</param>
        public void Log(
            LogLevel level,
            string message,
            string? overwriteCategory = null,
            Exception? exception = null,
            int eventId = default,
            [CallerFilePath] string? sourceFilePath = null,
            [CallerMemberName] string? memberName = null,
            [CallerLineNumber] int? sourceLineNumber = null) {
            RoleLogger.Log(
                level,
                message,
                in Context,
                overwriteCategory,
                exception,
                eventId,
                sourceFilePath,
                memberName,
                sourceLineNumber);
        }

        /// <summary>
        /// Emits a log message with the provided level, message, current correlation context information, and custom metadata.
        /// </summary>
        /// <param name="level">The severity level of the log message.</param>
        /// <param name="message">The log message text.</param>
        /// <param name="metadata">The custom metadata to include in the log.</param>
        /// <param name="overwriteCategory">An optional override for the default category.</param>
        /// <param name="exception">An optional exception to include in the log.</param>
        /// <param name="eventId">An optional event identifier.</param>
        /// <param name="sourceFilePath">The path of the source file that emitted the log (auto-filled).</param>
        /// <param name="memberName">The name of the member emitting the log (auto-filled).</param>
        /// <param name="sourceLineNumber">The source line number where the log was emitted (auto-filled).</param>
        public void Log(
            LogLevel level,
            string message,
            ReadOnlySpan<KeyValueMetadata> metadata,
            string? overwriteCategory = null,
            Exception? exception = null,
            int eventId = default,
            [CallerFilePath] string? sourceFilePath = null,
            [CallerMemberName] string? memberName = null,
            [CallerLineNumber] int? sourceLineNumber = null) {
            RoleLogger.Log(
                level,
                message,
                in Context,
                metadata,
                overwriteCategory,
                exception,
                eventId,
                sourceFilePath,
                memberName,
                sourceLineNumber);
        }
        /// <summary>
        /// Returns a string representation of the correlation context for debugging purposes.
        /// </summary>
        /// <returns>A string containing the correlation, trace, and span identifiers.</returns>
        public override string ToString() => Context.ToString();
    }
}
