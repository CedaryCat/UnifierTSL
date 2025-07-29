using NuGet.Common;
using NuGet.Configuration;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Packaging.Signing;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace UnifierTSL.Module.Dependencies
{
    public static class NugetPackageCache
    {
        private static readonly SourceCacheContext Cache = new();
        private static readonly ILogger Logger = NullLogger.Instance;
        private static readonly SourceRepository SourceRepository = Repository.Factory.GetCoreV3("https://api.nuget.org/v3/index.json");

        private static readonly ConcurrentDictionary<string, Task<string>> extractedPackages = new();

        public static async Task<string> GetDllPathAsync(string packageId, string version, string targetFramework, string dllName) {
            string packagePath = await EnsurePackageExtractedAsync(packageId, version);

            using var reader = new PackageFolderReader(packagePath);
            var libItems = await reader.GetLibItemsAsync(CancellationToken.None);

            var allFrameworks = libItems.Select(group => group.TargetFramework).ToList();

            var reducer = new FrameworkReducer();
            var target = NuGetFramework.ParseFolder(targetFramework);
            var nearest = reducer.GetNearest(target, allFrameworks) 
                ?? throw new InvalidOperationException($"No compatible frameworks found in package '{packageId}' for target '{targetFramework}'.");
            var compatibleLibGroup = libItems.First(g => g.TargetFramework.Equals(nearest));

            var relativeDllPath = compatibleLibGroup.Items
                .FirstOrDefault(path => Path.GetFileName(path).Equals(dllName, StringComparison.OrdinalIgnoreCase)) 
                ?? throw new FileNotFoundException($"DLL '{dllName}' not found in NuGet package '{packageId}' for framework '{nearest.GetShortFolderName()}'.");
            string fullPath = Path.Combine(packagePath, relativeDllPath);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException($"Expected DLL at path '{fullPath}' does not exist.");

            return fullPath;
        }


        private static async Task<string> EnsurePackageExtractedAsync(string packageId, string version) {
            var key = $"{packageId.ToLowerInvariant()}:{version}";
            return await extractedPackages.GetOrAdd(key, async _ =>
            {
                var settings = Settings.LoadDefaultSettings(root: null);
                var globalPackagesPath = SettingsUtility.GetGlobalPackagesFolder(settings);
                var versionFolder = Path.Combine(globalPackagesPath, packageId.ToLowerInvariant(), version);

                if (Directory.Exists(versionFolder))
                    return versionFolder;

                var resource = await SourceRepository.GetResourceAsync<FindPackageByIdResource>();
                using var stream = new MemoryStream();

                bool success = await resource.CopyNupkgToStreamAsync(
                    packageId, NuGetVersion.Parse(version), stream, Cache, Logger, CancellationToken.None);

                if (!success)
                    throw new Exception($"Failed to download package {packageId} {version} from NuGet.");

                stream.Position = 0;

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

                return versionFolder;
            });
        }
    }
}

