using UnifierTSL.PluginService.Dependencies;

namespace UnifierTSL.PluginService.Metadata
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public abstract class PluginDependenciesAttribute : Attribute
    {
        public abstract IDependencyProvider DependenciesProvider { get; }
    }
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class PluginDependenciesAttribute<TDependenciesProvider> : PluginDependenciesAttribute where TDependenciesProvider : IDependencyProvider, new()
    {
        public override IDependencyProvider DependenciesProvider => new TDependenciesProvider();
    }
}
