using System.Collections.Immutable;
using UnifierTSL.Plugins.Hosts.Dotnet;

namespace UnifierTSL.PluginService
{
    public abstract class BasePlugin : IPlugin
    {
        public virtual int InitializationOrder => 1;
        public abstract Task InitializeAsync(ReadOnlyMemory<PluginInitInfo> priorInitializations, CancellationToken cancellationToken = default);
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
