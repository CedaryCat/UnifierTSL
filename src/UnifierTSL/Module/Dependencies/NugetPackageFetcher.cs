using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using UnifierTSL.Logging;

namespace UnifierTSL.Module.Dependencies
{
    public sealed class NugetPackageFetcher(RoleLogger logger, string packageId, string version)
    {
        public readonly PackageIdentity PackageIdentity = new PackageIdentity(packageId, NuGetVersion.Parse(version));
        public string? PackagePath { get; private set; }
        public async Task EnsurePackageExtracted() {
            PackagePath = await NugetPackageCache.EnsurePackageExtractedAsync(logger, packageId, version);
        }
        public async Task<string[]> GetManagedLibsPathsAsync(string packageId, string version, string targetFramework) {
            PackagePath ??= await NugetPackageCache.EnsurePackageExtractedAsync(logger, packageId, version);

            using var reader = new PackageFolderReader(PackagePath);
            var libItems = await reader.GetLibItemsAsync(CancellationToken.None);

            var allFrameworks = libItems.Select(group => group.TargetFramework).ToList();

            var reducer = new FrameworkReducer();
            var target = NuGetFramework.ParseFolder(targetFramework);
            var nearest = reducer.GetNearest(target, allFrameworks);

            if (nearest is null) {
                // throw new InvalidOperationException($"No compatible frameworks found in package '{packageId}' for target '{targetFramework}'.");
                return [];
            }
            var compatibleLibGroup = libItems.First(g => g.TargetFramework.Equals(nearest));

            var paths = compatibleLibGroup.Items
                .Where(path => !string.Equals(Path.GetFileName(path), "_._", StringComparison.OrdinalIgnoreCase))
                .Select(path => Path.Combine(PackagePath, path).Replace('/', Path.DirectorySeparatorChar))
                .ToArray();
            return paths;
        }

        public async Task<string[]> GetNativeLibsPathsAsync(string packageId, string version, string runtimeIdentifier) {
            PackagePath ??= await NugetPackageCache.EnsurePackageExtractedAsync(logger, packageId, version);

            // Expand the runtime fallback graph (e.g., win-x64 -> win -> any)
            var expandedRids = RidGraph.Instance.ExpandRuntimeIdentifier(runtimeIdentifier).ToList();

            var nativeLibsByRid = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            // Get all files in the "runtimes" folder
            using var reader = new PackageFolderReader(PackagePath);

            var allFiles = await reader.GetFilesAsync(CancellationToken.None);

            foreach (var file in allFiles) {
                if (!file.StartsWith("runtimes/", StringComparison.OrdinalIgnoreCase))
                    continue;

                // match: runtimes/<rid>/native/<file>
                var parts = file.Split('/');

                if (parts.Length >= 4 &&
                    string.Equals(parts[2], "native", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(Path.GetFileName(file), "_._", StringComparison.OrdinalIgnoreCase)) {
                    var rid = parts[1];
                    if (!nativeLibsByRid.TryGetValue(rid, out var list)) {
                        nativeLibsByRid[rid] = list = [];
                    }

                    list.Add(file);
                }
            }

            // have no native libs
            if (nativeLibsByRid.Count == 0) {
                return [];
            }

            // Try fallback RIDs
            foreach (var rid in expandedRids) {
                if (nativeLibsByRid.TryGetValue(rid, out var paths)) {
                    return paths
                        .Select(p => Path.Combine(PackagePath, p).Replace('/', Path.DirectorySeparatorChar))
                        .ToArray();
                }
            }

            return [];
            // throw new InvalidOperationException($"No native libraries found in package '{packageId}' compatible with RID '{runtimeIdentifier}'.");
        }
    }
}

