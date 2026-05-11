using System.Reflection;
using System.Runtime.Loader;

namespace Atelier.Session.Execution
{
    internal sealed class ExecutionLoadContext : AssemblyLoadContext
    {
        private readonly Func<AssemblyName, Assembly?> resolveAssembly;
        private readonly Dictionary<string, Assembly> ownedAssembliesByFullName = new(StringComparer.OrdinalIgnoreCase);

        public ExecutionLoadContext(string name, Func<AssemblyName, Assembly?> resolveAssembly) : base(name, isCollectible: true) {
            this.resolveAssembly = resolveAssembly ?? throw new ArgumentNullException(nameof(resolveAssembly));
        }

        protected override Assembly? Load(AssemblyName assemblyName) {
            return TryResolveOwnedAssembly(assemblyName) ?? resolveAssembly(assemblyName);
        }

        public Assembly LoadAssembly(byte[] peImage, byte[] pdbImage) {

            using var peStream = new MemoryStream(peImage, writable: false);
            using var pdbStream = new MemoryStream(pdbImage, writable: false);
            return LoadFromStream(peStream, pdbStream);
        }

        public void RegisterOwnedAssembly(Assembly assembly) {

            if (assembly.GetName().FullName is { Length: > 0 } fullName) {
                ownedAssembliesByFullName[fullName] = assembly;
            }
        }

        private Assembly? TryResolveOwnedAssembly(AssemblyName assemblyName) {
            if (assemblyName.FullName is { Length: > 0 } fullName && ownedAssembliesByFullName.TryGetValue(fullName, out var exactMatch)) {
                return exactMatch;
            }

            foreach (var assembly in ownedAssembliesByFullName.Values) {
                if (AssemblyName.ReferenceMatchesDefinition(assembly.GetName(), assemblyName)) {
                    return assembly;
                }
            }

            return null;
        }
    }
}
