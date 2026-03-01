namespace UnifierTSL.Logging
{
    public interface IMetadataInjectionHost
    {
        void AddMetadataInjector(ILogMetadataInjector injector);
        void RemoveMetadataInjector(ILogMetadataInjector injector);
        IReadOnlyList<ILogMetadataInjector> MetadataInjectors { get; }
    }
}
