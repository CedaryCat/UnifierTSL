using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;

namespace UnifierTSL.PluginService.Dependencies
{
    public class NativeEmbeddedDependency : PluginDependency
    {
        private readonly Assembly plugin;
        private readonly Version version;
        private readonly string libraryName;
        private readonly string embeddedPath;
        private readonly string rid;
        private readonly string fileNameWithExt;
        public override string Name => libraryName;
        public override Version Version => version;
        public override DependencyKind Kind => DependencyKind.NativeLibrary;
        public override string ExpectedPath => Path.Combine(rid, "native", $"{fileNameWithExt}");
        public override IDependencyLibraryExtractor LibraryExtractor { get; }
        public NativeEmbeddedDependency(Assembly plugin, string libraryName, Version libraryVersion) {
            this.libraryName = libraryName;
            this.plugin = plugin;
            this.version = libraryVersion;

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

            LibraryExtractor = new EmbeddedLibraryResolver(plugin, libraryVersion, libraryName, embeddedPath);
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

        class EmbeddedLibraryResolver(Assembly plugin, Version version, string libraryName, string embeddedPath) : IDependencyLibraryExtractor
        {
            public string LibraryName => libraryName;
            public Version Version => version;
            public Stream Extract() {
                return plugin.GetManifestResourceStream(embeddedPath)!;
            }
        }
    }
}
