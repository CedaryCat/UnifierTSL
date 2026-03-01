namespace UnifierTSL.Logging
{
    public interface ILogMetadataInjector
    {
        void InjectMetadata(scoped ref LogEntry entry);
    }
}
