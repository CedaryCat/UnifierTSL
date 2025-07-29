using UnifierTSL.Module.Dependencies;

namespace UnifierTSL.Module
{
    public interface IDependencyProvider
    {
        public abstract IReadOnlyList<ModuleDependency> GetDependencies();
    }
}
