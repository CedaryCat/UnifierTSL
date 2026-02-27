using System.Collections.Immutable;
using NuGet.Versioning;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using UnifierTSL.FileSystem;
using UnifierTSL.Module.Dependencies;

namespace UnifierTSL.Module
{
    public class ModuleLoadContext : AssemblyLoadContext
    {
        private bool _disposed;
        private readonly AssemblyDependencyResolver UTSLResolver;
        private readonly Assembly hostAssembly = typeof(UnifierApi).Assembly;
        private readonly FileInfo moduleFile;
        public ModuleLoadContext(FileInfo moduleFile) : base(isCollectible: true) {
            UTSLResolver = new(hostAssembly.Location);
            this.moduleFile = moduleFile;
            Resolving += OnResolving;
            Unloading += OnUnloading;
            ResolvingUnmanagedDll += OnResolvingUnmanagedDll;
        }
        public override string ToString() => $"{moduleFile.Name} ({base.ToString()})";

        private ImmutableArray<Func<Task>> _disposeActions = [];
        public void AddDisposeAction(Func<Task> action) {
            if (_disposed) {
                throw new InvalidOperationException("Dispose has already been called.");
            }
            ImmutableInterlocked.Update(ref _disposeActions, x => x.Add(action));
        }
        private void OnUnloading(AssemblyLoadContext context) {
            _disposed = true;
            Task.WaitAll([.. _disposeActions.Select(x => x())]);
            _disposeActions = [];
        }
        private static bool IsFrameworkAssembly(AssemblyName assemblyName) {
            var token = BitConverter.ToString(assemblyName.GetPublicKeyToken() ?? Array.Empty<byte>());
            if (token is 
                "B0-3F-5F-7F-11-D5-0A-3A" or // System.Runtime.Loader
                "CC-7B-13-FF-CD-2D-DD-51" // netstandard
                )
                return true;
            return false;
        }

        protected virtual bool IsUTSLCoreLibs(AssemblyName assemblyName) {
            if (assemblyName.Name is null) {
                return false;
            }
            if (assemblyName.Name.StartsWith("Mono", StringComparison.Ordinal)) {
                return true;
            }
            if (assemblyName.Name.StartsWith("MonoMod", StringComparison.Ordinal)) {
                return true;
            }
            if (assemblyName.Name.StartsWith("OTAPI", StringComparison.Ordinal)) {
                return true;
            }
            if (assemblyName.Name.StartsWith("ModFramework", StringComparison.Ordinal)) {
                return true;
            }
            return false;
        }
        /// <summary>
        /// Raised when attempting to resolve a shared assembly with strict matching rules (name + version).
        /// Triggered before custom resolvers.
        /// </summary>
        public event Func<AssemblyLoadContext, AssemblyName, Assembly?>? ResolvingSharedAssemblyPreferred;

        /// <summary>
        /// Raised as a final fallback to resolve a shared assembly with relaxed matching rules (name only).
        /// Triggered after all other resolution attempts have failed.
        /// </summary>
        public event Func<AssemblyLoadContext, AssemblyName, Assembly?>? ResolvingSharedAssemblyFallback;

        private Assembly? OnResolving(AssemblyLoadContext context, AssemblyName name) {
            if (context != this) {
                return null;
            }
            return Load(name);
        }
        protected override Assembly? Load(AssemblyName assemblyName) {

            if (IsFrameworkAssembly(assemblyName)) {
                return Assembly.Load(assemblyName);
            }

            if (assemblyName.Name == hostAssembly.GetName().Name) {
                return hostAssembly;
            }

            string? utslCoreLibPath = UTSLResolver.ResolveAssemblyToPath(assemblyName);
            if (IsUTSLCoreLibs(assemblyName) && utslCoreLibPath is not null) {
                return LoadFromHostContext(assemblyName, utslCoreLibPath);
            }

            if (ResolvingSharedAssemblyPreferred is not null) {
                foreach (Func<AssemblyLoadContext, AssemblyName, Assembly?> resolver in ResolvingSharedAssemblyPreferred.GetInvocationList().Cast<Func<AssemblyLoadContext, AssemblyName, Assembly?>>()) {
                    Assembly? result = resolver(this, assemblyName);
                    if (result is not null) {
                        return result;
                    }
                }
            }

            string moduleDir = Path.GetDirectoryName(moduleFile.FullName)!;
            string matchFile = Path.Combine(moduleDir, "lib", assemblyName.Name + ".dll");

            if (File.Exists(matchFile)) {
                return LoadFromModuleContext(assemblyName, new FileInfo(matchFile).FullName);
            }

            if (utslCoreLibPath is not null) {
                return LoadFromHostContext(assemblyName, utslCoreLibPath);
            }

            if (ResolvingSharedAssemblyFallback is not null) {
                foreach (Func<AssemblyLoadContext, AssemblyName, Assembly?> resolver in ResolvingSharedAssemblyFallback.GetInvocationList().Cast<Func<AssemblyLoadContext, AssemblyName, Assembly?>>()) {
                    Assembly? result = resolver(this, assemblyName);
                    if (result is not null) {
                        return result;
                    }
                }
            }

            return base.Load(assemblyName);
        }

        private nint OnResolvingUnmanagedDll(Assembly pInvokeUser, string unmanagedDllName) {
            return LoadUnmanagedDll(unmanagedDllName);
        }

        protected override nint LoadUnmanagedDll(string unmanagedDllName) {
            string currentRid = RuntimeInformation.RuntimeIdentifier;
            IEnumerable<string> fallbackRids = RidGraph.Instance.ExpandRuntimeIdentifier(currentRid);

            string moduleDir = Path.GetDirectoryName(moduleFile.FullName)!;
            string extension = FileSystemHelper.GetLibraryExtension();

            DependenciesSetting config = DependenciesConfiguration.LoadDependenciesConfig(moduleDir);

            DependencyItem? match = config.Dependencies.Values
                .SelectMany(x => x.Manifests)
                .Where(x => !x.Obsolete)
                .Where(x => IsNativeManifestMatch(x.FilePath, unmanagedDllName, extension))
                .FirstOrDefault();

            if (match is not null) {
                return LoadUnmanagedDllFromPath(Path.Combine(moduleDir, match.FilePath));
            }

            return base.LoadUnmanagedDll(unmanagedDllName);
        }

        private static bool IsNativeManifestMatch(string manifestPath, string unmanagedDllName, string extension) {
            string fileName = Path.GetFileName(manifestPath);
            if (string.IsNullOrWhiteSpace(fileName)) {
                return false;
            }

            if (!string.Equals(Path.GetExtension(fileName), extension, StringComparison.OrdinalIgnoreCase)) {
                return false;
            }

            string requestName = NormalizeNativeLibraryName(unmanagedDllName);
            if (string.IsNullOrWhiteSpace(requestName)) {
                return false;
            }

            string manifestName = NormalizeNativeLibraryName(fileName);
            if (string.Equals(manifestName, requestName, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }

            if (!manifestName.StartsWith(requestName + ".", StringComparison.OrdinalIgnoreCase)) {
                return false;
            }

            string versionSuffix = manifestName[(requestName.Length + 1)..];
            return NuGetVersion.TryParse(versionSuffix, out _);
        }

        private static string NormalizeNativeLibraryName(string libraryName) {
            if (string.IsNullOrWhiteSpace(libraryName)) {
                return string.Empty;
            }

            string fileName = Path.GetFileName(libraryName.Trim());
            string ext = Path.GetExtension(fileName);
            if (string.IsNullOrWhiteSpace(ext)) {
                return fileName;
            }
            return fileName[..^ext.Length];
        }

        private Assembly LoadFromModuleContext(AssemblyName _, string assemblyPath) {
            using FileStream libStream = File.OpenRead(assemblyPath);
            if (File.Exists(Path.ChangeExtension(assemblyPath, ".pdb"))) {
                using FileStream pdbStream = File.OpenRead(Path.ChangeExtension(assemblyPath, ".pdb"));
                return LoadFromStream(libStream, pdbStream);
            }
            return LoadFromStream(libStream);
        }

        private static Assembly LoadFromHostContext(AssemblyName asmName, string assemblyPath) {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies()) {
                if (asm.GetName().Name == asmName.Name) {
                    return asm;
                }
            }
            return Default.LoadFromAssemblyPath(assemblyPath);
        }
    }
}
