using System.Collections.Immutable;
using UnifierTSL.PluginService.Dependencies;
using UnifierTSL.PluginService.Loading;

namespace UnifierTSL.PluginService
{
    public abstract class BasePlugin : IPlugin
    {
        public virtual int InitializationOrder => 1;
        public abstract Task InitializeAsync(ReadOnlySpan<PluginInitInfo> priorInitializations, CancellationToken cancellationToken = default);
        public virtual Task ShutdownAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() {
            GC.SuppressFinalize(this);
            return DisposeAsync(true);
        }
        public virtual ValueTask DisposeAsync(bool isDisposing) {
            return ValueTask.CompletedTask;
        }
        public virtual void BeforeGlobalInitialize(ImmutableArray<PluginContainer> plugins) { }
    }
}
