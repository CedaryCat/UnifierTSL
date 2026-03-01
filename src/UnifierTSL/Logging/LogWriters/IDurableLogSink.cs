namespace UnifierTSL.Logging.LogWriters
{
    internal interface IDurableLogSink : IDisposable
    {
        void WriteBatch(ReadOnlySpan<QueuedDurableLogRecord> records);

        void Flush();
    }
}
