using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using UnifierTSL.FileSystem;
using UnifierTSL.Module.Dependencies;

namespace UnifierTSL.Module
{
    public class ModuleLoadContext : AssemblyLoadContext, IDisposable
    {
        bool _disposed;
        readonly AssemblyDependencyResolver UTSLResolver;
        readonly Assembly hostAssembly = typeof(UnifierApi).Assembly;
        readonly FileInfo moduleFile;
        public ModuleLoadContext(FileInfo moduleFile) : base(isCollectible: true) {
            UTSLResolver = new(hostAssembly.Location);
            this.moduleFile = moduleFile;
            Resolving += OnResolving;
            ResolvingUnmanagedDll += OnResolvingUnmanagedDll;
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

            var moduleDir = Path.GetDirectoryName(moduleFile.FullName)!;
            var matchFile = Path.Combine(moduleDir, "lib", assemblyName.Name + ".dll");

            if (File.Exists(matchFile)) {
                return LoadFromModuleContext(assemblyName, new FileInfo(matchFile).FullName);
            }

            if (utslCoreLibPath is not null) {
                return LoadFromHostContext(assemblyName, utslCoreLibPath);
            }

            return base.Load(assemblyName);
        }

        private nint OnResolvingUnmanagedDll(Assembly pInvokeUser, string unmanagedDllName) {
            if (!Assemblies.Contains(pInvokeUser)) {
                return nint.Zero;
            }
            return LoadUnmanagedDll(unmanagedDllName);
        }

        protected override nint LoadUnmanagedDll(string unmanagedLibName) {
            var currentRid = RuntimeInformation.RuntimeIdentifier;
            var fallbackRids = RidGraph.Instance.ExpandRuntimeIdentifier(currentRid);

            var moduleDir = Path.GetDirectoryName(moduleFile.FullName)!;

            var fileName1 = FileSystemHelper.GetDynamicLibraryFileName(unmanagedLibName, withPrefix: true);
            var fileName2 = FileSystemHelper.GetDynamicLibraryFileName(unmanagedLibName, withPrefix: false);


            foreach (var rid in fallbackRids) {
                var currentPath = Path.Combine(moduleDir, "runtimes", rid, "native", fileName1);
                if (File.Exists(currentPath)) {
                    return LoadUnmanagedDllFromPath(currentPath);
                }
                currentPath = Path.Combine(moduleDir, "runtimes", rid, "native", fileName2);
                if (File.Exists(currentPath)) {
                    return LoadUnmanagedDllFromPath(currentPath);
                }
            }

            return base.LoadUnmanagedDll(unmanagedLibName);
        }

        Assembly LoadFromModuleContext(AssemblyName _, string assemblyPath) {
            return LoadFromAssemblyPath(assemblyPath);
        }

        readonly static ConcurrentDictionary<string, Assembly> HostAssemblyLoadCache = [];
        static Assembly LoadFromHostContext(AssemblyName asmName, string assemblyPath) {
            var name = asmName.Name!;
            return HostAssemblyLoadCache.GetOrAdd(name, _ => Default.LoadFromAssemblyPath(assemblyPath));
        }

        public void Dispose() {
            if (_disposed) return;

            _disposed = true;
            Unload();

            GC.SuppressFinalize(this);
        }
    }
}
