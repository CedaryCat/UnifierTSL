namespace Kaleido.Runtime
{
    internal sealed class OperationSet
    {
        private readonly Lock gate = new();
        private readonly HashSet<Task> operations = [];
        private bool accepting = true;

        public void Track(Task operation) {
            ArgumentNullException.ThrowIfNull(operation);
            lock (gate) {
                if (!accepting) {
                    throw new InvalidOperationException("The operation set is no longer accepting work.");
                }

                operations.Add(operation);
            }

            _ = RemoveWhenCompletedAsync(operation);
        }

        public async Task DrainAsync() {
            while (true) {
                Task[] snapshot;
                lock (gate) {
                    accepting = false;
                    snapshot = [.. operations];
                }

                if (snapshot.Length == 0) {
                    return;
                }

                try {
                    await Task.WhenAll(snapshot).ConfigureAwait(false);
                }
                catch {
                }
            }
        }

        private async Task RemoveWhenCompletedAsync(Task operation) {
            try {
                await operation.ConfigureAwait(false);
            }
            catch {
            }
            finally {
                lock (gate) {
                    operations.Remove(operation);
                }
            }
        }
    }
}
