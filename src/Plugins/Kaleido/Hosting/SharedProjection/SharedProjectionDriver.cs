using Kaleido.Model.Ids;
using Kaleido.Model.Lifecycle;
using Kaleido.Model.Transfer;

namespace Kaleido.Hosting.SharedProjection
{
    internal sealed class SharedProjectionDriver(SharedProjectionContext context) : IRealmDriver
    {
        private readonly Lock gate = new();
        private readonly HashSet<int> players = [];

        public RealmRuntimeState State { get; private set; } = RealmRuntimeState.Running;

        public Task WaitUntilReadyAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void Attach(RealmPlayer player, RealmEntry entry) {
            lock (gate) {
                players.Add(player.Id);
            }
        }

        public void Detach(RealmPlayer player, RealmExit exit) {
            lock (gate) {
                players.Remove(player.Id);
            }
        }

        public void Tick(RealmTick tick) {
            context.ProjectionDispatcher.Run(TickCore);
        }

        private void TickCore() {
            int[] snapshot;
            lock (gate) {
                snapshot = [.. players];
            }

            foreach (var playerId in snapshot) {
                try {
                    context.Main.player[playerId].active = true;
                    context.CheckBytes(playerId);
                    context.Input.InvokeFrame(playerId);
                }
                catch (Exception ex) {
                    context.Log.Error(category: "SharedProjection", message: $"Failed to update player #{playerId} in shared projection '{context.Name}'.", ex: ex);
                }
                finally {
                    context.Main.player[playerId].active = false;
                }
            }
        }

        public Task StopAsync(RealmRetireReason reason) {
            State = RealmRuntimeState.Stopped;
            return Task.CompletedTask;
        }

        public void Dispose() {
            context.Dispose();
        }
    }
}
