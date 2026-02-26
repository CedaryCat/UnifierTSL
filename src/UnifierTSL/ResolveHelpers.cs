using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using UnifierTSL.Module.Dependencies;

namespace UnifierTSL
{
    public static class ResolveHelpers
    {
        internal static Assembly? ResolveAssembly(AssemblyLoadContext context, AssemblyName name) {
            string libraryDir = Path.Combine(Directory.GetCurrentDirectory(), "lib");
            string fileName = Path.Combine(libraryDir, name.Name + ".dll");
            if (File.Exists(fileName)) {
                return context.LoadFromAssemblyPath(Path.Combine(libraryDir, name.Name + ".dll"));
            }
            if (name.Name != "Terraria" && name.Name != "OTAPI" && TryResolveTerrariaEmbeddedAssembly(context, name, out Assembly? assembly)) {
                return assembly;
            }
            return null;
        }

        private static bool TryResolveTerrariaEmbeddedAssembly(AssemblyLoadContext context, AssemblyName name, [NotNullWhen(true)] out Assembly? assembly) {
            Assembly terrariaAssembly = typeof(Terraria.Program).Assembly;
            string resourceName = name.Name + ".dll";
            string? text = Array.Find(terrariaAssembly.GetManifestResourceNames(), element => element.EndsWith(resourceName));
            if (text is not null) {
                using Stream stream = terrariaAssembly.GetManifestResourceStream(text)!;
                assembly = context.LoadFromStream(stream);
                return true;
            }
            assembly = null;
            return false;
        }

        internal static nint ResolveNativeDll(Assembly _, string unmanagedDllName) {
            string currentRid = RuntimeInformation.RuntimeIdentifier;
            IEnumerable<string> fallbackRids = RidGraph.Instance.ExpandRuntimeIdentifier(currentRid);

            string dir = Directory.GetCurrentDirectory();

            string extension = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".dll" :
                RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? ".so" :
                RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? ".dylib" :
                throw new PlatformNotSupportedException("Unsupported OS platform");

            HashSet<string> nativeNames = [];
            if (!string.IsNullOrWhiteSpace(unmanagedDllName)) {
                string original = unmanagedDllName.Trim();
                nativeNames.Add(original);

                if (!original.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) {
                    nativeNames.Add(original + extension);
                }
                else {
                    string withoutExt = Path.GetFileNameWithoutExtension(original);
                    if (!string.IsNullOrWhiteSpace(withoutExt)) {
                        nativeNames.Add(withoutExt + extension);
                    }
                }
            }

            foreach (string rid in fallbackRids) {
                foreach (string nativeName in nativeNames) {
                    string currentPath = Path.Combine(dir, "runtimes", rid, "native", nativeName);
                    if (File.Exists(currentPath)) {
                        return NativeLibrary.Load(currentPath);
                    }
                }
            }
            return nint.Zero;
        }
    }
}
