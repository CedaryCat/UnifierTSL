using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Host.Mef;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;

namespace Atelier.Session.Roslyn
{
    internal static class RoslynHost
    {
        private static readonly Lock Sync = new();
        private static readonly ImmutableArray<string> RequiredAssemblyNames = [
            "Microsoft.CodeAnalysis",
            "Microsoft.CodeAnalysis.Workspaces",
            "Microsoft.CodeAnalysis.CSharp",
            "Microsoft.CodeAnalysis.CSharp.Workspaces",
            "Microsoft.CodeAnalysis.Features",
            "Microsoft.CodeAnalysis.CSharp.Features",
            "Microsoft.CodeAnalysis.Scripting",
            "Microsoft.CodeAnalysis.CSharp.Scripting",
        ];
        private static MefHostServices? sharedHostServices;

        public static void Initialize() {
            _ = GetHostServices();
        }

        public static AdhocWorkspace CreateWorkspace() {
            return new AdhocWorkspace(GetHostServices());
        }

        private static MefHostServices GetHostServices() {
            lock (Sync) {
                return sharedHostServices ??= CreateHostServices();
            }
        }

        private static MefHostServices CreateHostServices() {
            var context = AssemblyLoadContext.GetLoadContext(typeof(RoslynHost).Assembly)
                ?? AssemblyLoadContext.Default;
            Dictionary<string, Assembly> compositionAssemblies = [];
            foreach (var assembly in MefHostServices.DefaultAssemblies) {
                var name = assembly.GetName().Name;
                if (!string.IsNullOrWhiteSpace(name)) {
                    compositionAssemblies[name] = assembly;
                }
            }

            List<string> missing = [];
            foreach (var assemblyName in RequiredAssemblyNames) {
                if (!TryLoadAssembly(context, assemblyName, out var assembly)) {
                    missing.Add(assemblyName);
                    continue;
                }

                compositionAssemblies[assemblyName] = assembly;
            }

            if (missing.Count > 0) {
                throw new InvalidOperationException(
                    GetString($"Atelier Roslyn host is missing required assemblies: {string.Join(", ", missing)}"));
            }

            return MefHostServices.Create([.. compositionAssemblies.Values]);
        }

        private static bool TryLoadAssembly(AssemblyLoadContext context, string assemblyName, out Assembly assembly) {
            foreach (var loadedAssembly in context.Assemblies) {
                if (string.Equals(loadedAssembly.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase)) {
                    assembly = loadedAssembly;
                    return true;
                }
            }

            try {
                assembly = context.LoadFromAssemblyName(new AssemblyName(assemblyName));
                return true;
            }
            catch {
                assembly = null!;
                return false;
            }
        }
    }
}
