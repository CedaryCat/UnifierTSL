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
            string key = $"{packageId.ToLowerInvariant()}:{version}";
            return await extractedPackages.GetOrAdd(key, async _ => {
                ISettings settings = Settings.LoadDefaultSettings(root: null);
                string globalPackagesPath = SettingsUtility.GetGlobalPackagesFolder(settings);
                string versionFolder = Path.Combine(globalPackagesPath, packageId.ToLowerInvariant(), version);

                if (Directory.Exists(versionFolder)) {
                    logger.Info($"Package: {packageId} ({version}) found in cache.");
                    return versionFolder;
                }

                logger.Info($"Downloading package: {packageId} ({version}) from NuGet.");

                FindPackageByIdResource resource = await SourceRepository.GetResourceAsync<FindPackageByIdResource>();
                using MemoryStream stream = new();

                bool success = await resource.CopyNupkgToStreamAsync(
                    packageId, NuGetVersion.Parse(version), stream, Cache, Logger, CancellationToken.None);

                if (!success)
                    throw new Exception($"Failed to download package {packageId} {version} from NuGet.");

                stream.Position = 0;

                logger.Info($"Extracting package: {packageId} ({version}) to local nuget cache.");
                using PackageArchiveReader reader = new(stream);

                // Use PackageExtractor to extract contents
                PackageIdentity packageIdentity = new(packageId, NuGetVersion.Parse(version));
                PackageExtractionContext context = new(
                    PackageSaveMode.Defaultv3,
                    XmlDocFileSaveMode.Skip,
                    ClientPolicyContext.GetClientPolicy(settings, Logger),
                    Logger
                );

                var packagePathResolver = new StandardPackagePathResolver(globalPackagesPath);

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

        public class StandardPackagePathResolver(string rootDirectory) : PackagePathResolver(rootDirectory)
        {
            public override string GetPackageDirectoryName(PackageIdentity packageIdentity) {
                return Path.Combine(packageIdentity.Id.ToLowerInvariant(), packageIdentity.Version.ToNormalizedString());
            }
            public override string GetInstallPath(PackageIdentity packageIdentity) {
                return Path.Combine(Root, GetPackageDirectoryName(packageIdentity));
            }
        }

        public static async Task<List<PackageIdentity>> ResolveDependenciesAsync(string packageId, string version, string targetFramework) {
            PackageIdentity rootPackage = new(packageId, NuGetVersion.Parse(version));
            Dictionary<string, PackageIdentity> resolved = new(StringComparer.OrdinalIgnoreCase);
            Queue<PackageIdentity> toResolve = new();
            toResolve.Enqueue(rootPackage);

            IEnumerable<Lazy<INuGetResourceProvider>> providers = Repository.Provider.GetCoreV3();
            ISettings settings = Settings.LoadDefaultSettings(root: null);
            string globalPackagesFolder = SettingsUtility.GetGlobalPackagesFolder(settings);
            FindLocalPackagesResourceV3 localRepo = new(globalPackagesFolder);

            PackageMetadataResource metadataResource = await SourceRepository.GetResourceAsync<PackageMetadataResource>();

            NuGetFramework target = NuGetFramework.ParseFolder(targetFramework);
            FrameworkReducer reducer = new();

            while (toResolve.Count > 0) {
                PackageIdentity current = toResolve.Dequeue();

                if (resolved.ContainsKey(current.Id))
                    continue;

                resolved[current.Id] = current;

                IEnumerable<PackageDependencyGroup>? dependencyGroups = null;

                // try to read nuspec from local cache
                var localPackage = localRepo.FindPackagesById(current.Id, NullLogger.Instance, CancellationToken.None)
                    .FirstOrDefault(pkg => pkg.Identity.Version == current.Version);
                if (localPackage != null) {
                    NuspecReader nuspecReader = localPackage.Nuspec;
                    dependencyGroups = nuspecReader.GetDependencyGroups();
                }
                else {
                    // cache miss, request remote source
                    IPackageSearchMetadata metadata = await metadataResource.GetMetadataAsync(current, new SourceCacheContext(), NullLogger.Instance, CancellationToken.None);
                    if (metadata != null) {
                        dependencyGroups = metadata.DependencySets;
                    }
                }

                if (dependencyGroups == null)
                    continue;

                NuGetFramework? nearest = reducer.GetNearest(target, dependencyGroups.Select(g => g.TargetFramework));
                if (nearest == null)
                    continue;

                IEnumerable<PackageIdentity> dependencies = dependencyGroups
                    .Where(g => g.TargetFramework.Equals(nearest))
                    .SelectMany(g => g.Packages)
                    .Select(d => new PackageIdentity(d.Id, d.VersionRange.MinVersion))
                    .Where(p => p != null);

                foreach (PackageIdentity? dep in dependencies) {
                    if (!resolved.ContainsKey(dep.Id))
                        toResolve.Enqueue(dep);
                }
            }

            return [.. resolved.Values];
        }
    }
}

