using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using UnifierTSL.Module;

namespace Atelier.Session.Roslyn
{
    internal static class MetadataReferenceCollector
    {
        public static MetadataReferenceSet Collect(
            IEnumerable<Assembly>? assemblyReferences = null,
            IEnumerable<string?>? assemblyPathReferences = null,
            IEnumerable<MetadataReference>? inMemoryReferences = null) {

            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var references = ImmutableArray.CreateBuilder<MetadataReference>();
            var referencePaths = ImmutableArray.CreateBuilder<string>();
            AddTrustedPlatformAssemblies(paths, references, referencePaths);
            AddDefaultContextAssemblies(paths, references, referencePaths);

            if (assemblyReferences is not null) {
                foreach (var assembly in assemblyReferences) {
                    AddAssembly(assembly, paths, references, referencePaths);
                }
            }

            if (assemblyPathReferences is not null) {
                foreach (var path in assemblyPathReferences) {
                    AddReference(path, paths, references, referencePaths);
                }
            }

            if (inMemoryReferences is not null) {
                references.AddRange(inMemoryReferences);
            }

            return new MetadataReferenceSet(references.ToImmutable(), referencePaths.ToImmutable());
        }

        private static void AddTrustedPlatformAssemblies(
            HashSet<string> paths,
            ImmutableArray<MetadataReference>.Builder references,
            ImmutableArray<string>.Builder referencePaths) {

            if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is not string trustedPlatformAssemblies
                || string.IsNullOrWhiteSpace(trustedPlatformAssemblies)) {
                return;
            }

            var splitOptions = StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries;
            foreach (var path in trustedPlatformAssemblies.Split(Path.PathSeparator, splitOptions)) {
                AddReference(path, paths, references, referencePaths);
            }
        }

        private static void AddDefaultContextAssemblies(
            HashSet<string> paths,
            ImmutableArray<MetadataReference>.Builder references,
            ImmutableArray<string>.Builder referencePaths) {

            foreach (var assembly in AssemblyLoadContext.Default.Assemblies) {
                AddAssembly(assembly, paths, references, referencePaths);
            }
        }

        private static void AddAssembly(
            Assembly assembly,
            HashSet<string> paths,
            ImmutableArray<MetadataReference>.Builder references,
            ImmutableArray<string>.Builder referencePaths) {

            if (assembly.IsDynamic) {
                return;
            }

            if (!string.IsNullOrWhiteSpace(assembly.Location)) {
                AddReference(assembly.Location, paths, references, referencePaths);
                return;
            }

            if (AssemblyLoadContext.GetLoadContext(assembly) is ModuleLoadContext moduleLoadContext
                && !string.IsNullOrWhiteSpace(moduleLoadContext.MainAssemblyPath)) {
                AddReference(moduleLoadContext.MainAssemblyPath, paths, references, referencePaths);
            }
        }

        private static void AddReference(
            string? path,
            HashSet<string> paths,
            ImmutableArray<MetadataReference>.Builder references,
            ImmutableArray<string>.Builder referencePaths) {

            if (string.IsNullOrWhiteSpace(path) || !paths.Add(path) || !File.Exists(path)) {
                return;
            }

            referencePaths.Add(path);
            var documentationPath = TryResolveDocumentationPath(path);
            references.Add(File.Exists(documentationPath)
                ? MetadataReference.CreateFromFile(path, documentation: XmlDocumentationProvider.CreateFromFile(documentationPath))
                : MetadataReference.CreateFromFile(path));
        }

        private static string TryResolveDocumentationPath(string path) {
            var localDocumentationPath = Path.ChangeExtension(path, ".xml");
            if (File.Exists(localDocumentationPath)) {
                return localDocumentationPath;
            }

            return TryResolveSharedFrameworkDocumentationPath(path) ?? string.Empty;
        }

        private static string? TryResolveSharedFrameworkDocumentationPath(string assemblyPath) {
            var assemblyDirectory = new DirectoryInfo(Path.GetDirectoryName(assemblyPath) ?? string.Empty);
            var frameworkDirectory = assemblyDirectory.Parent;
            var sharedDirectory = frameworkDirectory?.Parent;
            if (frameworkDirectory is null
                || sharedDirectory is null
                || !string.Equals(sharedDirectory.Name, "shared", StringComparison.OrdinalIgnoreCase)
                || sharedDirectory.Parent is null) {
                return null;
            }

            var runtimeRoot = sharedDirectory.Parent.FullName;
            var packRoot = Path.Combine(runtimeRoot, "packs", frameworkDirectory.Name + ".Ref", assemblyDirectory.Name, "ref");
            return Directory.Exists(packRoot)
                ? Directory
                    .GetFiles(packRoot, Path.GetFileNameWithoutExtension(assemblyPath) + ".xml", SearchOption.AllDirectories)
                    .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
                    .LastOrDefault()
                : null;
        }
    }

    internal sealed record MetadataReferenceSet(
        ImmutableArray<MetadataReference> References,
        ImmutableArray<string> ReferencePaths);
}
