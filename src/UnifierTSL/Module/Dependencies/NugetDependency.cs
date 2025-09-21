using NuGet.Frameworks;
using NuGet.Versioning;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using UnifierTSL.Logging;

namespace UnifierTSL.Module.Dependencies
{
    public sealed class NugetDependency : ModuleDependency
    {
        private readonly string packageId;
        private readonly NuGetVersion version;
        private readonly string targetFramework;

        public NugetDependency(Assembly plugin, string packageId, string version) {
            this.packageId = packageId;
            this.version = NuGetVersion.Parse(version);
            targetFramework = GetNuGetShortFolderName(plugin) ?? throw new Exception("");
        }
        public NugetDependency(Assembly plugin, string packageId, NuGetVersion version) {
            this.packageId = packageId;
            this.version = version;
            targetFramework = GetNuGetShortFolderName(plugin) ?? throw new Exception("");
        }
        public NugetDependency(Assembly plugin, string packageId, Version version) {
            this.packageId = packageId;
            this.version = new(version);
            targetFramework = GetNuGetShortFolderName(plugin) ?? throw new Exception("");
        }
        private static string? GetNuGetShortFolderName(Assembly assembly) {
            TargetFrameworkAttribute? attr = assembly.GetCustomAttribute<TargetFrameworkAttribute>();
            if (attr == null) return null;

            NuGetFramework framework = NuGetFramework.Parse(attr.FrameworkName);
            return framework.GetShortFolderName();
        }

        public override string Name => packageId;
        public override NuGetVersion Version => version;

        public override IDependencyLibraryExtractor LibraryExtractor => new NugetLibraryExtractor(packageId, version, targetFramework);

        private class NugetLibraryExtractor(string packageId, NuGetVersion version, string targetFramework) : IDependencyLibraryExtractor
        {
            public async Task<ImmutableArray<LibraryEntry>> Extract(RoleLogger logger) {
                logger.Info($"Resolving dependencies for {packageId} ({version})");
                List<NuGet.Packaging.Core.PackageIdentity> packages = NugetPackageCache.ResolveDependenciesAsync(packageId, version.ToNormalizedString(), targetFramework)
                    .GetAwaiter()
                    .GetResult();
                logger.Success($"Resolved dependencies for {packageId} ({version}) successfully: \r\n{string.Join("\r\n", packages)}");
                List<LibraryEntry> entries = [];

                foreach (NuGet.Packaging.Core.PackageIdentity package in packages) {
                    NugetPackageFetcher downloader = new(logger, package.Id, package.Version.ToNormalizedString());
                    foreach (string lib in await downloader.GetManagedLibsPathsAsync(package.Id, package.Version.ToNormalizedString(), targetFramework)) {
                        entries.Add(new LibraryEntry(
                            new Lazy<Stream>(() => File.OpenRead(lib)),
                            DependencyKind.ManagedAssembly,
                            Path.Combine("lib", Path.GetFileName(lib)),
                            package.Version,
                            package.Id));
                    }
                    string currentRid = RuntimeInformation.RuntimeIdentifier;
                    foreach (string lib in await downloader.GetNativeLibsPathsAsync(package.Id, package.Version.ToNormalizedString(), currentRid)) {
                        DirectoryInfo nativeDirInfo = new(Path.GetDirectoryName(lib)!);
                        // path: runtimes/<rid>/native/<file>
                        if (nativeDirInfo.Name != "native" || nativeDirInfo.Parent!.Parent!.Name != "runtimes") {
                            throw new InvalidOperationException("Invalid package structure.");
                        }
                        string rid = nativeDirInfo.Parent!.Name;

                        entries.Add(new LibraryEntry(
                            new Lazy<Stream>(() => File.OpenRead(lib)),
                            DependencyKind.NativeLibrary,
                            Path.Combine("runtimes", rid, "native", Path.GetFileName(lib)),
                            package.Version,
                            package.Id));
                    }
                }

                return [.. entries];
            }

            ImmutableArray<LibraryEntry> IDependencyLibraryExtractor.Extract(RoleLogger logger) {
                return Extract(logger).GetAwaiter().GetResult();
            }
        }
    }
}
