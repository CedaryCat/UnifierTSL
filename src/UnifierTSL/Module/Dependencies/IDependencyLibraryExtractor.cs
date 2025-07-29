namespace UnifierTSL.Module.Dependencies
{
    public interface IDependencyLibraryExtractor
    {
        string LibraryName { get; }
        Version Version { get; }
        public Stream Extract();
    }
}
