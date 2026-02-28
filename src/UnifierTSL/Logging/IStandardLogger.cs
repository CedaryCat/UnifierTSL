using System.Runtime.CompilerServices;
using UnifierTSL.Logging.Metadata;

namespace UnifierTSL.Logging
{
    /// <summary>
    /// Defines a standard, extensible logging abstraction that supports structured logging, source context capture,
    /// and customizable severity levels.
    ///
    /// <para>
    /// This interface provides two core logging methods:
    /// one for simple messages and one for messages enriched with structured metadata.
    /// The interface is designed to be extended via <c>LoggerExt</c> extension methods,
    /// which enable concise and expressive logging calls like <c>logger.Info("message")</c>
    /// without sacrificing performance (e.g., no boxing via generic constraints).
    /// </para>
    /// 
    /// </summary>
    public interface IStandardLogger
    {
        /// <summary>
        /// Emits a log message with the provided level, message.
        /// </summary>
        /// <param name="level">The severity level of the log message.</param>
        /// <param name="message">The log message text.</param>
        /// <param name="overwriteCategory">An optional override for the default category.</param>
        /// <param name="exception">An optional exception to include in the log.</param>
        /// <param name="eventId">An optional event identifier.</param>
        /// <param name="sourceFilePath">The path of the source file that emitted the log (auto-filled).</param>
        /// <param name="memberName">The name of the member emitting the log (auto-filled).</param>
        /// <param name="sourceLineNumber">The source line number where the log was emitted (auto-filled).</param>
        void Log(
            LogLevel level,
            string message,
            string? overwriteCategory = null,
            Exception? exception = null,
            int eventId = default,
            [CallerFilePath] string? sourceFilePath = null,
            [CallerMemberName] string? memberName = null,
            [CallerLineNumber] int? sourceLineNumber = null);

        /// <summary>
        /// Emits a log message with the provided level, message, and metadata.
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
        void Log(
            LogLevel level,
            string message,
            ReadOnlySpan<KeyValueMetadata> metadata,
            string? overwriteCategory = null,
            Exception? exception = null,
            int eventId = default,
            [CallerFilePath] string? sourceFilePath = null,
            [CallerMemberName] string? memberName = null,
            [CallerLineNumber] int? sourceLineNumber = null);
    }
}
