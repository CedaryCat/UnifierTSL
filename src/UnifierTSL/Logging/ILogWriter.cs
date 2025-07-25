using UnifierTSL.Logging.Formatters;

namespace UnifierTSL.Logging
{
    /// <summary>
    /// Provides a mechanism for log writers to expose available formatters and write log entries.
    /// </summary>
    public interface ILogWriter
    {
        /// <summary>
        /// Retrieves the collection of available formatters that can be used by this writer.
        /// </summary>
        /// <param name="availableFormatters">An output parameter that receives the available formatters.</param>
        void GetAvailableFormatters(out FormatterSelectionContext availableFormatters);

        /// <summary>
        /// Writes the specified log entry using the current formatter.
        /// </summary>
        /// <param name="log">The log entry to be written.</param>
        void Write(LogEntry log);
    }
}
