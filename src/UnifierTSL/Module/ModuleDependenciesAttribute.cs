namespace UnifierTSL.Module
{
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
    public abstract class ModuleDependenciesAttribute : Attribute
    {
        // Ensure that the attribute is not inherited by external assemblies
        internal ModuleDependenciesAttribute() { }
        public abstract IDependencyProvider DependenciesProvider { get; }
    }
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
    public sealed class ModuleDependenciesAttribute<TDependenciesProvider> : ModuleDependenciesAttribute where TDependenciesProvider : IDependencyProvider, new()
    {
        public override IDependencyProvider DependenciesProvider => new TDependenciesProvider();
    }
}
