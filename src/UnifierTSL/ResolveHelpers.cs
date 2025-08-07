using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using UnifierTSL.Module.Dependencies;

namespace UnifierTSL
{
    public static class ResolveHelpers
    {
        internal static Assembly? ResolveAssembly(AssemblyLoadContext context, AssemblyName name) {
            var terrariaAssembly = typeof(Terraria.Program).Assembly;
            string resourceName = name.Name + ".dll";
            string? text = Array.Find(terrariaAssembly.GetManifestResourceNames(), element => element.EndsWith(resourceName));
            if (text is not null) {
                using var stream = terrariaAssembly.GetManifestResourceStream(text)!;
                var assembly = context.LoadFromStream(stream);
                return assembly;
            }
            return null;
        }

        internal static nint ResolveNativeDll(Assembly _, string unmanagedDllName) {
            var currentRid = RuntimeInformation.RuntimeIdentifier;
            var fallbackRids = RidGraph.Instance.ExpandRuntimeIdentifier(currentRid);

            var dir = Directory.GetCurrentDirectory();

            string extension = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".dll" :
                RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? ".so" :
                RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? ".dylib" :
                throw new PlatformNotSupportedException("Unsupported OS platform");

            foreach (var rid in fallbackRids) {
                var currentPath = Path.Combine(dir, "runtimes", rid, "native", unmanagedDllName + extension);
                if (File.Exists(currentPath)) {
                    return NativeLibrary.Load(currentPath);
                }
            }
            return nint.Zero;
        }
    }
}