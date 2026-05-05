namespace UnifierTSL.Surface.Activities;

public sealed class SurfaceActivityScope : IDisposable, IAsyncDisposable
{
    private readonly IDisposable? innerScope;
    private int disposed;

    internal SurfaceActivityScope(
        ActivityHandle? activity,
        IDisposable? innerScope,
        CancellationToken cancellationToken) {
        Activity = activity;
        this.innerScope = innerScope;
        CancellationToken = activity?.CancellationToken ?? cancellationToken;
    }

    public ActivityHandle? Activity { get; }

    public CancellationToken CancellationToken { get; }

    public void Dispose() {
        if (Interlocked.Exchange(ref disposed, 1) != 0) {
            return;
        }

        try {
            innerScope?.Dispose();
        }
        catch {
        }
    }

    public ValueTask DisposeAsync() {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
