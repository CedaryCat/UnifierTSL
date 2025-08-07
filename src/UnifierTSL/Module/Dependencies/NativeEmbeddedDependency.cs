using NuGet.Versioning;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;
using UnifierTSL.Logging;

namespace UnifierTSL.Module.Dependencies
{
    public class NativeEmbeddedDependency : ModuleDependency
    {
        private readonly Assembly plugin;
        private readonly NuGetVersion version;
        private readonly string libraryName;
        private readonly string embeddedPath;
        private readonly string rid;
        private readonly string fileNameWithExt;
        public override string Name => libraryName;
        public override NuGetVersion Version => version;
        public override IDependencyLibraryExtractor LibraryExtractor { get; }
        public NativeEmbeddedDependency(Assembly plugin, string libraryName, NuGetVersion libraryVersion) {
            this.libraryName = libraryName;
            this.plugin = plugin;
            version = libraryVersion;

            var currentRid = RuntimeInformation.RuntimeIdentifier;
            var fallbackRids = RidGraph.Instance.ExpandRuntimeIdentifier(currentRid);

            foreach (var rid in fallbackRids) {
                if (TryExtractNativeLibrary(plugin, libraryName, rid, out var fileNameWithExt, out var resourcePath)) {
                    this.rid = rid;
                    embeddedPath = resourcePath;
                    this.fileNameWithExt = fileNameWithExt;
                    break;
                }
            }

            if (embeddedPath is null || fileNameWithExt is null || rid is null) {
                throw new Exception($"Unable to find native library '{libraryName}' for RID '{currentRid}' in embedded resources.");
            }

            LibraryExtractor = new EmbeddedLibraryResolver(plugin, libraryVersion, rid, fileNameWithExt, libraryName, embeddedPath);
        }
        static bool TryExtractNativeLibrary(Assembly plugin, string libraryName, string rid,
            [NotNullWhen(true)] out string? fileNameWithExt,
            [NotNullWhen(true)] out string? resourcePath) {

            var baseResourceName = $"Native\\libs\\{libraryName}\\{rid}\\";
            resourcePath = plugin.GetManifestResourceNames().FirstOrDefault(n => n.StartsWith(baseResourceName));

            if (resourcePath == null) {
                fileNameWithExt = null;
                return false;
            }

            var fileNameParts = resourcePath[baseResourceName.Length..].Split('\\');
            fileNameWithExt = fileNameParts.Last();
            return true;
        }

        class EmbeddedLibraryResolver(Assembly plugin, NuGetVersion version, string rid, string fileNameWithExt, string libraryName, string embeddedPath) : IDependencyLibraryExtractor
        {
            public ImmutableArray<LibraryEntry> Extract(RoleLogger logger) {
                return [
                    new LibraryEntry(
                        new(() => plugin.GetManifestResourceStream(embeddedPath)!),
                        DependencyKind.NativeLibrary,
                        Path.Combine("runtimes", rid, "native", $"{fileNameWithExt}"),
                        version,
                        libraryName
                    )
                ];
            }
        }
    }
}
