namespace UnifierTSL;

public static class CompositeDisposableExtension {
    extension(IDisposable) { 
        public static IDisposable operator +(IDisposable? left, IDisposable right) {
            return CompositeDisposable.Create(left, right);
        }
    }
}

public sealed class CompositeDisposable : IDisposable
{
    private IDisposable[]? disposables;

    public CompositeDisposable(params IDisposable?[] disposables)
        : this((IEnumerable<IDisposable?>)disposables) {
    }

    public CompositeDisposable(IEnumerable<IDisposable?> disposables) {
        this.disposables = Normalize(disposables);
    }

    public static IDisposable Create(params IDisposable?[] disposables) {
        return Create((IEnumerable<IDisposable?>)disposables);
    }

    public static IDisposable Create(IEnumerable<IDisposable?> disposables) {
        IDisposable[] normalized = Normalize(disposables);
        return normalized.Length switch {
            0 => EmptyDisposable.Instance,
            1 => normalized[0],
            _ => new CompositeDisposable(normalized, normalized: true),
        };
    }

    public void Dispose() {
        IDisposable[]? current = Interlocked.Exchange(ref disposables, null);
        if (current is null || current.Length == 0) {
            return;
        }

        List<Exception>? exceptions = null;
        for (int i = current.Length - 1; i >= 0; i--) {
            try {
                current[i].Dispose();
            }
            catch (Exception ex) {
                (exceptions ??= []).Add(ex);
            }
        }

        if (exceptions is not null) {
            throw new AggregateException(exceptions);
        }
    }

    private CompositeDisposable(IDisposable[] disposables, bool normalized) {
        this.disposables = disposables;
    }

    private static IDisposable[] Normalize(IEnumerable<IDisposable?> disposables) {

        List<IDisposable> normalized = [];
        foreach (IDisposable? disposable in disposables) {
            if (disposable is not null && !ReferenceEquals(disposable, EmptyDisposable.Instance)) {
                normalized.Add(disposable);
            }
        }

        return [.. normalized];
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static EmptyDisposable Instance { get; } = new();

        public void Dispose() {
        }
    }
}
