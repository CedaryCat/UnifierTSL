using NuGet.Common;
using NuGet.Configuration;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Packaging.Signing;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using System.Collections.Concurrent;
using UnifierTSL.Logging;

namespace UnifierTSL.Module.Dependencies
{
    public static class NugetPackageCache
    {
        private static readonly SourceCacheContext Cache = new();
        private static readonly ILogger Logger = NullLogger.Instance;
        private static readonly SourceRepository SourceRepository = Repository.Factory.GetCoreV3("https://api.nuget.org/v3/index.json");

        private static readonly ConcurrentDictionary<string, Task<string>> extractedPackages = new();
        public static async Task<string> EnsurePackageExtractedAsync(RoleLogger logger, string packageId, string version) {
            var key = $"{packageId.ToLowerInvariant()}:{version}";
            return await extractedPackages.GetOrAdd(key, async _ =>
            {
                var settings = Settings.LoadDefaultSettings(root: null);
                var globalPackagesPath = SettingsUtility.GetGlobalPackagesFolder(settings);
                var versionFolder = Path.Combine(globalPackagesPath, packageId.ToLowerInvariant(), version);

                if (Directory.Exists(versionFolder)) {
                    logger.Info($"Package: {packageId} ({version}) found in cache.");
                    return versionFolder;
                }

                logger.Info($"Downloading package: {packageId} ({version}) from NuGet.");

                var resource = await SourceRepository.GetResourceAsync<FindPackageByIdResource>();
                using var stream = new MemoryStream();

                bool success = await resource.CopyNupkgToStreamAsync(
                    packageId, NuGetVersion.Parse(version), stream, Cache, Logger, CancellationToken.None);

                if (!success)
                    throw new Exception($"Failed to download package {packageId} {version} from NuGet.");

                stream.Position = 0;

                logger.Info($"Extracting package: {packageId} ({version}) to local nuget cache.");
                using var reader = new PackageArchiveReader(stream);

                // Use PackageExtractor to extract contents
                var packageIdentity = new PackageIdentity(packageId, NuGetVersion.Parse(version));
                var context = new PackageExtractionContext(
                    PackageSaveMode.Defaultv3,
                    XmlDocFileSaveMode.Skip,
                    ClientPolicyContext.GetClientPolicy(settings, Logger),
                    Logger
                );

                var packagePathResolver = new PackagePathResolver(globalPackagesPath);

                await PackageExtractor.ExtractPackageAsync(
                    source: null,
                    packageReader: reader,
                    packagePathResolver: packagePathResolver,
                    packageExtractionContext: context,
                    token: CancellationToken.None
                );

                logger.Info($"Extracted package: {packageId} ({version}) successfully.");

                return versionFolder;
            });
        }
        public static async Task<List<PackageIdentity>> ResolveDependenciesAsync(string packageId, string version, string targetFramework) {
            var rootPackage = new PackageIdentity(packageId, NuGetVersion.Parse(version));
            var resolved = new Dictionary<string, PackageIdentity>(StringComparer.OrdinalIgnoreCase);
            var toResolve = new Queue<PackageIdentity>();
            toResolve.Enqueue(rootPackage);

            var providers = Repository.Provider.GetCoreV3();
            var settings = Settings.LoadDefaultSettings(root: null);
            var globalPackagesFolder = SettingsUtility.GetGlobalPackagesFolder(settings);
            var localRepo = new FindLocalPackagesResourceV3(globalPackagesFolder);

            var metadataResource = await SourceRepository.GetResourceAsync<PackageMetadataResource>();

            var target = NuGetFramework.ParseFolder(targetFramework);
            var reducer = new FrameworkReducer();

            while (toResolve.Count > 0) {
                var current = toResolve.Dequeue();

                if (resolved.ContainsKey(current.Id))
                    continue;

                resolved[current.Id] = current;

                IEnumerable<PackageDependencyGroup>? dependencyGroups = null;

                // try to read nuspec from local cache
                var localPackage = localRepo.FindPackagesById(current.Id, NullLogger.Instance, CancellationToken.None)
                    .First(pkg => pkg.Identity.Version == current.Version);
                if (localPackage != null) {
                    var nuspecReader = localPackage.Nuspec;
                    dependencyGroups = nuspecReader.GetDependencyGroups();
                }
                else {
                    // cache miss, request remote source
                    var metadata = await metadataResource.GetMetadataAsync(current, new SourceCacheContext(), NullLogger.Instance, CancellationToken.None);
                    if (metadata != null) {
                        dependencyGroups = metadata.DependencySets;
                    }
                }

                if (dependencyGroups == null)
                    continue;

                var nearest = reducer.GetNearest(target, dependencyGroups.Select(g => g.TargetFramework));
                if (nearest == null)
                    continue;

                var dependencies = dependencyGroups
                    .Where(g => g.TargetFramework.Equals(nearest))
                    .SelectMany(g => g.Packages)
                    .Select(d => new PackageIdentity(d.Id, d.VersionRange.MinVersion))
                    .Where(p => p != null);

                foreach (var dep in dependencies) {
                    if (!resolved.ContainsKey(dep.Id))
                        toResolve.Enqueue(dep);
                }
            }

            return [.. resolved.Values];
        }
    }
}

