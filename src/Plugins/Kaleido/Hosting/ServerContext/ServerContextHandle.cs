using Kaleido.Model.Ids;
using Kaleido.Model.Lifecycle;
using Kaleido.Model.Transfer;
using UnifierTSL.Servers;
namespace Kaleido.Hosting.ServerContext
{
    internal sealed class ServerContextHandle(UnifierTSL.Servers.ServerContext server) : IRealmDriver
    {
        private UnifierTSL.Servers.ServerContext Server { get; } = server;
        public RealmRuntimeState State { get; private set; } = RealmRuntimeState.Running;

        public async Task WaitUntilReadyAsync(CancellationToken cancellationToken) {
            while (!Server.IsRunning) {
                cancellationToken.ThrowIfCancellationRequested();
                if (Server.RunningThread is { IsAlive: false }) {
                    throw new InvalidOperationException($"Realm server '{Server.Name}' stopped before it became ready.");
                }

                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
        }

        public void Attach(RealmPlayer player, RealmEntry entry) { }

        public void Detach(RealmPlayer player, RealmExit exit) { }

        public void Tick(RealmTick tick) { }

        public async Task StopAsync(RealmRetireReason reason) {
            if (State is RealmRuntimeState.Stopped or RealmRuntimeState.Stopping) {
                return;
            }

            State = RealmRuntimeState.Stopping;
            ServerRuntime.Unregister(Server);
            await Server.Close().ConfigureAwait(false);
            State = RealmRuntimeState.Stopped;
        }

        public void Dispose() {
            if (State != RealmRuntimeState.Stopped) {
                throw new InvalidOperationException($"Realm server '{Server.Name}' must be stopped before its driver is disposed.");
            }
        }
    }
}
