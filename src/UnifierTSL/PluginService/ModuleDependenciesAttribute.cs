using UnifierTSL.PluginService.Dependencies;

namespace UnifierTSL.PluginService.Metadata
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public abstract class ModuleDependenciesAttribute : Attribute
    {
        public abstract IDependencyProvider DependenciesProvider { get; }
    }
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class ModuleDependenciesAttribute<TDependenciesProvider> : ModuleDependenciesAttribute where TDependenciesProvider : IDependencyProvider, new()
    {
        public override IDependencyProvider DependenciesProvider => new TDependenciesProvider();
    }
}
