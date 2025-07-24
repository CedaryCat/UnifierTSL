using UnifierTSL.PluginService.Dependencies;

namespace UnifierTSL.PluginService
{
    public interface IDependencyProvider
    {
        public abstract IReadOnlyList<PluginDependency> GetDependencies();
    }
}
