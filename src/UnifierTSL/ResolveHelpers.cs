using System.Reflection;
using System.Runtime.Loader;

namespace UnifierTSL
{
    public static class ResolveHelpers
    {
        internal static Assembly? GlobalResolveAssembly(object? sender, ResolveEventArgs args) {
            var terrariaAssembly = typeof(Terraria.Program).Assembly;
            string resourceName = new AssemblyName(args.Name).Name + ".dll";
            string? text = Array.Find(terrariaAssembly.GetManifestResourceNames(), element => element.EndsWith(resourceName));
            if (text is not null) {
                using var stream = terrariaAssembly.GetManifestResourceStream(text)!;
                byte[] array = new byte[stream.Length];
                stream.ReadExactly(array);
                var assembly = Assembly.Load(array);
                return assembly;
            }
            text = new DirectoryInfo(Directory.GetCurrentDirectory()).GetFiles().FirstOrDefault(x => x.Name.EndsWith(resourceName))?.FullName;
            if (text is not null) {
                using var fileStream = new FileStream(text, FileMode.Open, FileAccess.Read);
                byte[] array = new byte[fileStream.Length];
                fileStream.ReadExactly(array);
                return Assembly.Load(array);
            }
            return null;
        }
    }
}