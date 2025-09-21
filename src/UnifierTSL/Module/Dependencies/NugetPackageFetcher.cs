using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using UnifierTSL.Logging;

namespace UnifierTSL.Module.Dependencies
{
    public sealed class NugetPackageFetcher(RoleLogger logger, string packageId, string version)
    {
        public readonly PackageIdentity PackageIdentity = new(packageId, NuGetVersion.Parse(version));
        public string? PackagePath { get; private set; }
        public async Task EnsurePackageExtracted() {
            PackagePath = await NugetPackageCache.EnsurePackageExtractedAsync(logger, packageId, version);
        }
        public async Task<string[]> GetManagedLibsPathsAsync(string packageId, string version, string targetFramework) {
            PackagePath ??= await NugetPackageCache.EnsurePackageExtractedAsync(logger, packageId, version);

            using PackageFolderReader reader = new(PackagePath);
            IEnumerable<FrameworkSpecificGroup> libItems = await reader.GetLibItemsAsync(CancellationToken.None);

            List<NuGetFramework> allFrameworks = libItems.Select(group => group.TargetFramework).ToList();

            FrameworkReducer reducer = new();
            NuGetFramework target = NuGetFramework.ParseFolder(targetFramework);
            NuGetFramework? nearest = reducer.GetNearest(target, allFrameworks);

            if (nearest is null) {
                // throw new InvalidOperationException($"No compatible frameworks found in package '{packageId}' for target '{targetFramework}'.");
                return [];
            }
            FrameworkSpecificGroup compatibleLibGroup = libItems.First(g => g.TargetFramework.Equals(nearest));

            string[] paths = compatibleLibGroup.Items
                .Where(path => !string.Equals(Path.GetFileName(path), "_._", StringComparison.OrdinalIgnoreCase))
                .Select(path => Path.Combine(PackagePath, path).Replace('/', Path.DirectorySeparatorChar))
                .ToArray();
            return paths;
        }

        public async Task<string[]> GetNativeLibsPathsAsync(string packageId, string version, string runtimeIdentifier) {
            PackagePath ??= await NugetPackageCache.EnsurePackageExtractedAsync(logger, packageId, version);

            // Expand the runtime fallback graph (e.g., win-x64 -> win -> any)
            List<string> expandedRids = RidGraph.Instance.ExpandRuntimeIdentifier(runtimeIdentifier).ToList();

            Dictionary<string, List<string>> nativeLibsByRid = new(StringComparer.OrdinalIgnoreCase);

            // Get all files in the "runtimes" folder
            using PackageFolderReader reader = new(PackagePath);

            IEnumerable<string> allFiles = await reader.GetFilesAsync(CancellationToken.None);

            foreach (string? file in allFiles) {
                if (!file.StartsWith("runtimes/", StringComparison.OrdinalIgnoreCase))
                    continue;

                // match: runtimes/<rid>/native/<file>
                string[] parts = file.Split('/');

                if (parts.Length >= 4 &&
                    string.Equals(parts[2], "native", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(Path.GetFileName(file), "_._", StringComparison.OrdinalIgnoreCase)) {
                    string rid = parts[1];
                    if (!nativeLibsByRid.TryGetValue(rid, out List<string>? list)) {
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
            foreach (string? rid in expandedRids) {
                if (nativeLibsByRid.TryGetValue(rid, out List<string>? paths)) {
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

