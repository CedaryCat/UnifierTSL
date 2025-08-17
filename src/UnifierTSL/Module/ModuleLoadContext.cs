using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using UnifierTSL.FileSystem;
using UnifierTSL.Module.Dependencies;

namespace UnifierTSL.Module
{
    public class ModuleLoadContext : AssemblyLoadContext
    {
        bool _disposed;
        readonly AssemblyDependencyResolver UTSLResolver;
        readonly Assembly hostAssembly = typeof(UnifierApi).Assembly;
        readonly FileInfo moduleFile;
        public ModuleLoadContext(FileInfo moduleFile) : base(isCollectible: true) {
            UTSLResolver = new(hostAssembly.Location);
            this.moduleFile = moduleFile;
            Resolving += OnResolving;
            Unloading += OnUnloading;
            ResolvingUnmanagedDll += OnResolvingUnmanagedDll;
        }
        ImmutableArray<Func<Task>> _disposeActions = [];
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
            if (assemblyName.Name == hostAssembly.GetName().Name) {
                return hostAssembly;
            }

            var utslCoreLibPath = UTSLResolver.ResolveAssemblyToPath(assemblyName);
            if (IsUTSLCoreLibs(assemblyName) && utslCoreLibPath is not null) {
                return LoadFromHostContext(assemblyName, utslCoreLibPath);
            }

            if (ResolvingSharedAssemblyPreferred is not null) {
                foreach (var resolver in ResolvingSharedAssemblyPreferred.GetInvocationList().Cast<Func<AssemblyLoadContext, AssemblyName, Assembly?>>()) {
                    var result = resolver(this, assemblyName);
                    if (result is not null) {
                        return result;
                    }
                }
            }

            var moduleDir = Path.GetDirectoryName(moduleFile.FullName)!;
            var matchFile = Path.Combine(moduleDir, "lib", assemblyName.Name + ".dll");

            if (File.Exists(matchFile)) {
                return LoadFromModuleContext(assemblyName, new FileInfo(matchFile).FullName);
            }

            if (utslCoreLibPath is not null) {
                return LoadFromHostContext(assemblyName, utslCoreLibPath);
            }

            if (ResolvingSharedAssemblyFallback is not null) {
                foreach (var resolver in ResolvingSharedAssemblyFallback.GetInvocationList().Cast<Func<AssemblyLoadContext, AssemblyName, Assembly?>>()) {
                    var result = resolver(this, assemblyName);
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

        protected override nint LoadUnmanagedDll(string unmanagedLibName) {
            var currentRid = RuntimeInformation.RuntimeIdentifier;
            var fallbackRids = RidGraph.Instance.ExpandRuntimeIdentifier(currentRid);

            var moduleDir = Path.GetDirectoryName(moduleFile.FullName)!;
            var extension = FileSystemHelper.GetLibraryExtension();

            var config = DependenciesConfiguration.LoadDependenicesConfig(moduleDir);

            var match = config.Dependencies.Values
                .SelectMany(x => x.Manifests)
                .Where(x => !x.Obsolete)
                .Where(x => Path.GetFileName(x.FilePath).StartsWith(unmanagedLibName + "."))
                .Where(x => Path.GetExtension(x.FilePath) == extension)
                .FirstOrDefault();

            if (match is not null) { 
                return LoadUnmanagedDllFromPath(Path.Combine(moduleDir, match.FilePath));
            }

            return base.LoadUnmanagedDll(unmanagedLibName);
        }

        Assembly LoadFromModuleContext(AssemblyName _, string assemblyPath) {
            using var libStream = File.OpenRead(assemblyPath);
            if (File.Exists(Path.ChangeExtension(assemblyPath, ".pdb"))) {
                using var pdbStream = File.OpenRead(Path.ChangeExtension(assemblyPath, ".pdb"));
                return LoadFromStream(libStream, pdbStream);
            }
            return LoadFromStream(libStream);
        }

        static Assembly LoadFromHostContext(AssemblyName asmName, string assemblyPath) {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) { 
                if (asm.GetName().Name == asmName.Name) {
                    return asm;
                }
            }
            return Default.LoadFromAssemblyPath(assemblyPath);
        }
    }
}
