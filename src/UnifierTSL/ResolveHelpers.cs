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

        static readonly Dictionary<string, Assembly> resolvedAssembly = []; // <AssemblyName, Assembly>
        internal static Assembly? ResolveAssembly(AssemblyLoadContext context, AssemblyName name) {
            if (name?.Name is null) return null;
            if (resolvedAssembly.TryGetValue(name.Name, out Assembly? asm) && asm is not null) return asm;

            var location = Path.Combine(AppContext.BaseDirectory, "bin", name.Name + ".dll");
            if (File.Exists(location))
                asm = context.LoadFromAssemblyPath(location);

            location = Path.ChangeExtension(location, ".exe");
            if (File.Exists(location))
                asm = context.LoadFromAssemblyPath(location);

            if (asm is not null)
                resolvedAssembly[name.Name] = asm;

            return asm;
        }
    }
}