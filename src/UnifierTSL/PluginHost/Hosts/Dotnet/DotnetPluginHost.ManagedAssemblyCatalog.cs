using System.Collections.Immutable;
using System.Reflection;
using UnifierTSL.PluginService;

namespace UnifierTSL.PluginHost.Hosts.Dotnet
{
    public partial class DotnetPluginHost : IManagedPluginAssemblyCatalog
    {
        public event EventHandler<ManagedPluginAssembliesInvalidatingEventArgs>? AssembliesInvalidating;

        ImmutableArray<ManagedPluginAssemblyDescriptor> IManagedPluginAssemblyCatalog.GetAttachableAssemblies() {
            return [.. Plugins
                .Where(IsAttachableManagedPlugin)
                .Select(CreateManagedAssemblyDescriptor)
                .Distinct(ManagedPluginAssemblyDescriptorStableKeyComparer.Instance)];
        }

        bool IManagedPluginAssemblyCatalog.TryResolveDescriptor(AssemblyName assemblyName, out ManagedPluginAssemblyDescriptor descriptor) {
            foreach (var plugin in Plugins) {
                if (!IsAttachableManagedPlugin(plugin) || !MatchesAssemblyIdentity(plugin.PluginAssembly.GetName(), assemblyName)) {
                    continue;
                }

                descriptor = CreateManagedAssemblyDescriptor(plugin);
                return true;
            }

            descriptor = null!;
            return false;
        }

        bool IManagedPluginAssemblyCatalog.TryResolveAttachedAssembly(IReadOnlyCollection<string> attachedAssemblyKeys, AssemblyName assemblyName, out Assembly assembly) {
            foreach (var plugin in Plugins) {
                if (!IsAttachableManagedPlugin(plugin) || !attachedAssemblyKeys.Any(key => string.Equals(key, CreateManagedAssemblyStableKey(plugin), StringComparison.Ordinal))) {
                    continue;
                }

                if (MatchesAssemblyIdentity(plugin.PluginAssembly.GetName(), assemblyName)) {
                    assembly = plugin.PluginAssembly;
                    return true;
                }

                if (TryResolveAssemblyFromContext(plugin, assemblyName, out assembly)) {
                    return true;
                }
            }

            assembly = null!;
            return false;
        }

        bool IManagedPluginAssemblyCatalog.TryGetAssembly(string stableKey, out Assembly assembly) {
            foreach (var plugin in Plugins) {
                if (!IsAttachableManagedPlugin(plugin) || !string.Equals(CreateManagedAssemblyStableKey(plugin), stableKey, StringComparison.Ordinal)) {
                    continue;
                }

                assembly = plugin.PluginAssembly;
                return true;
            }

            assembly = null!;
            return false;
        }

        internal void NotifyManagedAssembliesInvalidating(IEnumerable<PluginContainer> plugins) {
            var stableKeys = plugins
                .Where(IsAttachableManagedPlugin)
                .Select(CreateManagedAssemblyStableKey)
                .Distinct(StringComparer.Ordinal)
                .ToImmutableArray();
            if (stableKeys.IsDefaultOrEmpty) {
                return;
            }

            AssembliesInvalidating?.Invoke(this, new ManagedPluginAssembliesInvalidatingEventArgs(stableKeys));
        }

        private static bool TryResolveAssemblyFromContext(PluginContainer plugin, AssemblyName assemblyName, out Assembly assembly) {
            foreach (var loadedAssembly in plugin.Module.Context.Assemblies) {
                if (!MatchesAssemblyIdentity(loadedAssembly.GetName(), assemblyName)) {
                    continue;
                }

                assembly = loadedAssembly;
                return true;
            }

            try {
                assembly = plugin.Module.Context.LoadFromAssemblyName(assemblyName);
                return true;
            }
            catch {
                assembly = null!;
                return false;
            }
        }

        private static ManagedPluginAssemblyDescriptor CreateManagedAssemblyDescriptor(PluginContainer plugin) {
            var assemblyName = plugin.PluginAssembly.GetName();
            return new ManagedPluginAssemblyDescriptor(
                CreateManagedAssemblyStableKey(plugin),
                plugin.Name,
                assemblyName.Name ?? string.Empty,
                assemblyName.FullName ?? assemblyName.Name ?? string.Empty,
                plugin.Location.FilePath);
        }

        private static string CreateManagedAssemblyStableKey(PluginContainer plugin) {
            var assemblyName = plugin.PluginAssembly.GetName().Name ?? string.Empty;
            return $"{plugin.Location.FilePath}|{assemblyName}";
        }

        private static bool MatchesAssemblyIdentity(AssemblyName candidate, AssemblyName requested) {
            if (AssemblyName.ReferenceMatchesDefinition(candidate, requested)) {
                return true;
            }

            return string.Equals(candidate.FullName, requested.FullName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate.Name, requested.Name, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAttachableManagedPlugin(PluginContainer plugin) {
            return plugin.LoadStatus == PluginLoadStatus.Loaded && !plugin.Module.Unloaded;
        }

        private sealed class ManagedPluginAssemblyDescriptorStableKeyComparer : IEqualityComparer<ManagedPluginAssemblyDescriptor>
        {
            public static ManagedPluginAssemblyDescriptorStableKeyComparer Instance { get; } = new();

            public bool Equals(ManagedPluginAssemblyDescriptor? x, ManagedPluginAssemblyDescriptor? y) {
                return string.Equals(x?.StableKey, y?.StableKey, StringComparison.Ordinal);
            }

            public int GetHashCode(ManagedPluginAssemblyDescriptor obj) {
                return StringComparer.Ordinal.GetHashCode(obj.StableKey);
            }
        }
    }
}
