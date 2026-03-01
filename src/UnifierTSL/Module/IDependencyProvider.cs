using UnifierTSL.Module.Dependencies;

namespace UnifierTSL.Module
{
    public interface IDependencyProvider
    {
        abstract IReadOnlyList<ModuleDependency> GetDependencies();
    }
}
