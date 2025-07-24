namespace UnifierTSL.PluginService.Dependencies
{
    public interface IDependencyLibraryExtractor
    {
        string LibraryName { get; }
        Version Version { get; }
        public Stream Extract();
    }
}
