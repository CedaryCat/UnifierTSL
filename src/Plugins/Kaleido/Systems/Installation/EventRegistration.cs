namespace Kaleido.Systems.Installation
{
    internal sealed class EventRegistration(Action dispose) : IDisposable
    {
        private int disposed;

        public void Dispose() {
            if (Interlocked.Exchange(ref disposed, 1) == 0) {
                dispose();
            }
        }
    }
}
