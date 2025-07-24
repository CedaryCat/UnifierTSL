using NuGet.Common;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using System.Reflection;
using System.Runtime.Versioning;

namespace UnifierTSL.PluginService.Dependencies
{
    public class NugetManagedDependency : PluginDependency
    {
        private readonly string packageId;
        private readonly Version version;
        private readonly string targetFramework;
        private readonly string dllName;
        private readonly Lazy<Task<string>> dllPathLazy;

        public NugetManagedDependency(Assembly plugin, string packageId, Version version, string dllName) {
            this.packageId = packageId;
            this.version = version;
            this.dllName = dllName;
            targetFramework = GetNuGetShortFolderName(plugin) ?? throw new Exception("");
            dllPathLazy = new(() => NugetPackageCache.GetDllPathAsync(packageId, version.ToString(), targetFramework, dllName));
        }
        static string? GetNuGetShortFolderName(Assembly assembly) {
            var attr = assembly.GetCustomAttribute<TargetFrameworkAttribute>();
            if (attr == null) return null;

            var framework = NuGetFramework.Parse(attr.FrameworkName);
            return framework.GetShortFolderName();
        }

        public override string Name => packageId;
        public override Version Version => version;
        public override DependencyKind Kind => DependencyKind.ManagedAssembly;
        public override string ExpectedPath => Path.Combine(dllName);

        public override IDependencyLibraryExtractor LibraryExtractor =>
            new NugetLibraryExtractor(dllName, version, dllPathLazy);

        private class NugetLibraryExtractor(string dllName, Version version, Lazy<Task<string>> pathLazy)
            : IDependencyLibraryExtractor
        {
            public string LibraryName => dllName;
            public Version Version => version;

            public Stream Extract() {
                var path = pathLazy.Value.GetAwaiter().GetResult(); // Lazy + blocking resolve
                return File.OpenRead(path);
            }
        }
    }
}
