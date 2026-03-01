using NuGet.Versioning;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using UnifierTSL.Logging;
using UnifierTSL.Reflection.Metadata;

namespace UnifierTSL.Module.Dependencies
{
    public sealed class ManagedEmbeddedDependency(Assembly plugin, string embeddedPath) : ModuleDependency
    {
        private class EmbeddedLibraryExtractor : IDependencyLibraryExtractor
        {
            private readonly Assembly plugin;
            private readonly string embeddedPath;
            public readonly string LibraryName;
            public readonly NuGetVersion Version;

            public EmbeddedLibraryExtractor(Assembly plugin, string embeddedPath) {
                this.plugin = plugin;
                this.embeddedPath = embeddedPath;

                using Stream stream = Extract();
                using PEReader? peReader = MetadataBlobHelpers.GetPEReader(stream);
                if (peReader is not null && MetadataBlobHelpers.TryReadAssemblyIdentity(peReader.GetMetadataReader(), out string? name, out Version? version)) {
                    LibraryName = name;
                    Version = new NuGetVersion(version);
                }
                else {
                    throw new InvalidOperationException($"Unable to read assembly identity from embedded resource '{embeddedPath}'. Ensure it is a valid .NET assembly.");
                }
            }

            public Stream Extract() {
                return plugin.GetManifestResourceStream(embeddedPath) ?? throw new FileNotFoundException($"Embedded resource '{embeddedPath}' not found.");
            }

            ImmutableArray<LibraryEntry> IDependencyLibraryExtractor.Extract(RoleLogger logger) {
                return [
                    new LibraryEntry(
                        new Lazy<Stream>(() => plugin.GetManifestResourceStream(embeddedPath) ?? throw new FileNotFoundException($"Embedded resource '{embeddedPath}' not found.")),
                        DependencyKind.ManagedAssembly,
                        Path.Combine("lib", LibraryName + ".dll"),
                        Version,
                        LibraryName
                    )
                ];
            }
        }
        public override string Name => extractor.LibraryName;
        public override NuGetVersion Version => new(extractor.Version);

        private readonly EmbeddedLibraryExtractor extractor = new(plugin, embeddedPath);
        public override IDependencyLibraryExtractor LibraryExtractor => extractor;
    }
}
