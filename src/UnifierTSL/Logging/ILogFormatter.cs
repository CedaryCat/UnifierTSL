namespace UnifierTSL.Logging
{
    public interface IInspectableFormatter
    {
        string FormatName { get; }
        string Description { get; }
        dynamic Sample(scoped in LogEntry entry);
    }
    /// <summary>
    /// Defines a formatter that formats a <see cref="LogEntry"/> into a specified output buffer type.
    /// </summary>
    /// <typeparam name="TOutput">The output buffer type. Must be non-null.</typeparam>
    public interface ILogFormatter<TOutput> : IInspectableFormatter where TOutput : notnull
    {
        /// <summary>
        /// Formats the specified <see cref="LogEntry"/> into the provided output buffer.
        /// </summary>
        /// <param name="entry">The log entry to format.</param>
        /// <param name="buffer">The output buffer to write the formatted result into.</param>
        /// <param name="written">When this method returns, contains the number of elements written to the buffer.</param>
        /// <remarks>
        /// It is recommended to call <see cref="GetEstimatedSize(in LogEntry)"/> before this method
        /// to ensure that the provided buffer is large enough to hold the formatted output.
        /// </remarks>
        void Format(scoped in LogEntry entry, scoped ref TOutput buffer, out int written);

        /// <summary>
        /// Estimates the minimum number of elements required in the output buffer to successfully format the specified <see cref="LogEntry"/>.
        /// </summary>
        /// <param name="entry">The log entry for which to estimate the required buffer size.</param>
        /// <returns>The estimated number of elements needed in the output buffer.</returns>
        /// <remarks>
        /// Use this method before calling <see cref="Format(in LogEntry, ref TOutput, out int)"/> to avoid buffer overflows
        /// and ensure the buffer has sufficient capacity.
        /// </remarks>
        int GetEstimatedSize(scoped in LogEntry entry);
        dynamic IInspectableFormatter.Sample(in LogEntry entry) {
            TOutput[] formatted = new TOutput[GetEstimatedSize(entry)];
            Format(entry, ref formatted[0], out int written);
            return formatted.AsSpan(0, written).ToArray();
        }
    }
}
