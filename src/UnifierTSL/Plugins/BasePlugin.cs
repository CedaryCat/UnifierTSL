using System.Collections.Immutable;
using UnifierTSL.PluginService;

namespace UnifierTSL.Plugins
{
    public abstract class BasePlugin : IPlugin
    {
        public virtual int InitializationOrder => 1;
        public abstract Task InitializeAsync(IPluginConfigRegistrar configRegistrar, ReadOnlyMemory<PluginInitInfo> priorInitializations, CancellationToken cancellationToken = default);
        public virtual Task ShutdownAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() {
            GC.SuppressFinalize(this);
            return DisposeAsync(true);
        }
        public virtual ValueTask DisposeAsync(bool isDisposing) {
            return ValueTask.CompletedTask;
        }
        public virtual void BeforeGlobalInitialize(ImmutableArray<IPluginContainer> plugins) { }
    }
}
