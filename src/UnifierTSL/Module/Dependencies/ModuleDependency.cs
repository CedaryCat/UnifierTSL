using NuGet.Versioning;

namespace UnifierTSL.Module.Dependencies
{
    public abstract class ModuleDependency
    {
        internal ModuleDependency() { }
        public abstract string Name { get; }
        public abstract NuGetVersion Version { get; }
        /// <summary>
        /// Provide an abstraction for extracting dependent files.
        /// </summary>
        public abstract IDependencyLibraryExtractor LibraryExtractor { get; }
    }
}
