namespace UnifierTSL.Module.Dependencies
{
    public abstract class ModuleDependency
    {
        public abstract string Name { get; }
        public abstract Version Version { get; }
        public abstract DependencyKind Kind { get; }
        /// <summary>
        /// Provide an abstraction for extracting dependent files.
        /// </summary>
        public abstract IDependencyLibraryExtractor LibraryExtractor { get; }
        /// <summary>
        /// The expected path to store the extracted dependency file.
        /// </summary>
        public abstract string ExpectedPath { get; }
    }
}
