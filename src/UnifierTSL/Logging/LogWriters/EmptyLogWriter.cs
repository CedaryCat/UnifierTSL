namespace UnifierTSL.Logging.LogWriters
{
    internal sealed class EmptyLogWriter : LogWriter
    {
        public static readonly EmptyLogWriter Instance = new();

        public override void Write(scoped in LogEntry log) {
        }
    }
}
