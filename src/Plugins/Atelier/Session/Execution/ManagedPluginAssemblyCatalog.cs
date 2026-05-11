using System.Collections.Immutable;
using System.Reflection;
using UnifierTSL.PluginHost;

namespace Atelier.Session.Execution
{
    internal sealed class ManagedPluginAssemblyCatalog : IDisposable
    {
        private readonly ImmutableArray<IManagedPluginAssemblyCatalog> catalogs;

        public ManagedPluginAssemblyCatalog(IEnumerable<IManagedPluginAssemblyCatalog> catalogs) {

            this.catalogs = [.. catalogs];
            foreach (var catalog in this.catalogs) {
                catalog.AssembliesInvalidating += OnAssembliesInvalidating;
            }
        }

        public event Action<ImmutableArray<string>>? AssembliesInvalidating;

        public ImmutableArray<ManagedPluginReference> CaptureSnapshot() {
            return [.. catalogs
                .SelectMany(static catalog => catalog.GetAttachableAssemblies())
                .GroupBy(static descriptor => descriptor.StableKey, StringComparer.Ordinal)
                .Select(static group => group.First())
                .Select(static descriptor => new ManagedPluginReference(
                    descriptor.StableKey,
                    descriptor.DisplayName,
                    new AssemblyName(descriptor.RootAssemblyFullName),
                    descriptor.AssemblyPath))];
        }

        public bool TryResolveAttachedAssembly(IReadOnlyCollection<string> attachedAssemblyKeys, AssemblyName assemblyName, out Assembly assembly) {
            foreach (var catalog in catalogs) {
                if (catalog.TryResolveAttachedAssembly(attachedAssemblyKeys, assemblyName, out assembly)) {
                    return true;
                }
            }

            assembly = null!;
            return false;
        }

        public bool TryGetAssembly(string stableKey, out Assembly assembly) {
            foreach (var catalog in catalogs) {
                if (catalog.TryGetAssembly(stableKey, out assembly)) {
                    return true;
                }
            }

            assembly = null!;
            return false;
        }

        public void Dispose() {
            foreach (var catalog in catalogs) {
                catalog.AssembliesInvalidating -= OnAssembliesInvalidating;
            }
        }

        private void OnAssembliesInvalidating(object? sender, ManagedPluginAssembliesInvalidatingEventArgs e) {
            if (e.StableKeys.IsDefaultOrEmpty) {
                return;
            }

            AssembliesInvalidating?.Invoke([.. e.StableKeys.Distinct(StringComparer.Ordinal)]);
        }
    }
}
