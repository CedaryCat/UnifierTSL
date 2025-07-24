using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using UnifierTSL.Reflection.Metadata;

namespace UnifierTSL.PluginService.Dependencies
{
    public class ManagedEmbeddedDependency(Assembly plugin, string embeddedPath) : PluginDependency
    {
        public class EmbeddedLibraryExtractor : IDependencyLibraryExtractor
        {
            private readonly Assembly plugin;
            private readonly string embeddedPath;

            public EmbeddedLibraryExtractor(Assembly plugin, string embeddedPath) {
                this.plugin = plugin;
                this.embeddedPath = embeddedPath;

                using var stream = Extract();

                if (MetadataBlobHelpers.TryReadAssemblyIdentity(stream, out var name, out var version)) {
                    LibraryName = name;
                    Version = version;
                }
                else {
                    throw new InvalidOperationException($"Unable to read assembly identity from embedded resource '{embeddedPath}'. Ensure it is a valid .NET assembly.");
                }
            }

            public string LibraryName { get; }
            public Version Version { get; }

            public Stream Extract() {
                return plugin.GetManifestResourceStream(embeddedPath) ?? throw new FileNotFoundException($"Embedded resource '{embeddedPath}' not found.");
            }
        }
        public override string Name => LibraryExtractor.LibraryName;
        public override Version Version => LibraryExtractor.Version;
        public override DependencyKind Kind => DependencyKind.ManagedAssembly;
        public override string ExpectedPath => Path.Combine(LibraryExtractor.LibraryName + ".dll");
        public override IDependencyLibraryExtractor LibraryExtractor { get; } = new EmbeddedLibraryExtractor(plugin, embeddedPath);
    }
}
