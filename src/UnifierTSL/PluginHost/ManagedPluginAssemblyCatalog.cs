using System.Collections.Immutable;
using System.Reflection;

namespace UnifierTSL.PluginHost
{
    public interface IManagedPluginAssemblyCatalog
    {
        ImmutableArray<ManagedPluginAssemblyDescriptor> GetAttachableAssemblies();

        bool TryResolveDescriptor(AssemblyName assemblyName, out ManagedPluginAssemblyDescriptor descriptor);

        bool TryResolveAttachedAssembly(IReadOnlyCollection<string> attachedAssemblyKeys, AssemblyName assemblyName, out Assembly assembly);

        bool TryGetAssembly(string stableKey, out Assembly assembly);

        event EventHandler<ManagedPluginAssembliesInvalidatingEventArgs>? AssembliesInvalidating;
    }

    public sealed record ManagedPluginAssemblyDescriptor(
        string StableKey,
        string DisplayName,
        string RootAssemblyName,
        string RootAssemblyFullName,
        string AssemblyPath);

    public sealed class ManagedPluginAssembliesInvalidatingEventArgs(ImmutableArray<string> stableKeys) : EventArgs
    {
        public ImmutableArray<string> StableKeys { get; } = stableKeys;
    }
}
