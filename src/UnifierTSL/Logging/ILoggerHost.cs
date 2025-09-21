namespace UnifierTSL.Logging
{
    public interface ILoggerHost
    {
        string Name { get; }
        string? CurrentLogCategory { get; }
    }
}
