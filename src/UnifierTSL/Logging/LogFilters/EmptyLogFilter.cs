namespace UnifierTSL.Logging.LogFilters
{
    public class EmptyLogFilter : ILogFilter
    {
        public bool ShouldLog(in LogEntry entry) => true;
        public static readonly EmptyLogFilter Instance = new();
    }
}
