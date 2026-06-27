namespace Kaleido.Runtime
{
    internal sealed class RealmAdmission(RealmRuntime runtime) : IDisposable
    {
        private int disposed;

        public RealmRuntime Runtime { get; } = runtime;

        public void Dispose() {
            if (Interlocked.Exchange(ref disposed, 1) == 0) {
                Runtime.ExitAdmission();
            }
        }
    }
}
