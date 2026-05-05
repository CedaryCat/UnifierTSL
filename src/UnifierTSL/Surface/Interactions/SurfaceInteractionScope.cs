using UnifierTSL.Contracts.Projection;
using UnifierTSL.Contracts.Protocol.Payloads;
using UnifierTSL.Contracts.Sessions;
using UnifierTSL.Surface.Hosting;

namespace UnifierTSL.Surface.Interactions;

public readonly record struct SurfaceInteractionTermination(
    LifecyclePhase Phase);

public readonly record struct SurfaceInteractionScopeOptions(
    string InteractionKind,
    bool IsTransient = false);

public class SurfaceInteractionScope : IDisposable
{
    private ProjectionDocument? currentDocument;

    private bool started;
    private bool completed;
    private bool disposed;

    protected ISurfaceSession Session { get; }
    protected Lock Sync { get; } = new();

    public SurfaceInteractionScope(
        ISurfaceSession session,
        SurfaceInteractionScopeOptions options) {
        ArgumentNullException.ThrowIfNull(session);
        if (string.IsNullOrWhiteSpace(options.InteractionKind)) {
            throw new ArgumentException(GetString("Surface interaction kind must be provided."), nameof(options));
        }

        Session = session;
        Scope = Session.OpenInteractionScope(
            options.InteractionKind,
            isTransient: options.IsTransient);
        Session.PresentationAttached += OnPresentationAttached;
        Session.LifecycleReceived += OnLifecycleReceived;
    }

    public Contracts.Sessions.InteractionScope Scope { get; }

    public event Action<SurfaceInteractionTermination>? Terminated;

    public void PublishDocument(ProjectionDocument document) {
        ArgumentNullException.ThrowIfNull(document);
        var shouldPublish = false;
        lock (Sync) {
            ThrowIfDisposed();
            if (completed) {
                return;
            }

            currentDocument = document;
            shouldPublish = started && Session.IsPresentationAttached;
        }

        if (shouldPublish) {
            Session.PublishProjectionSnapshot(new ProjectionSnapshotPayload {
                Body = new ProjectionFullSnapshotBody {
                    Document = document,
                },
            });
        }
    }

    public void Start() {
        ProjectionDocument? document;
        lock (Sync) {
            ThrowIfDisposed();
            if (completed || started) {
                return;
            }

            started = true;
            document = currentDocument;
        }

        PublishLifecycle(LifecyclePhase.Active);
        if (document is not null) {
            PublishDocument(document);
        }
    }

    public void Complete() {
        lock (Sync) {
            if (disposed || completed) {
                return;
            }

            completed = true;
        }

        PublishLifecycle(LifecyclePhase.Completed);
    }

    public void Dispose() {
        var shouldRelease = false;
        lock (Sync) {
            if (disposed) {
                return;
            }

            disposed = true;
            shouldRelease = !completed;
            completed = true;
        }

        DetachSessionHandlers();
        if (!shouldRelease) {
            return;
        }

        try {
            PublishLifecycle(LifecyclePhase.Released);
        }
        catch {
        }
    }

    protected bool CanAcceptSessionEvent() {
        lock (Sync) {
            return !disposed && !completed;
        }
    }

    protected virtual void DetachSessionHandlers() {
        Session.PresentationAttached -= OnPresentationAttached;
        Session.LifecycleReceived -= OnLifecycleReceived;
    }

    protected void ThrowIfDisposed() {
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    private void PublishLifecycle(LifecyclePhase phase) {
        Session.PublishSurfaceOperation(SurfaceOperations.Lifecycle(new LifecyclePayload {
            InteractionScopeId = Scope.Id,
            Phase = phase,
            InteractionKind = Scope.Kind,
            IsTransient = Scope.IsTransient,
        }));
    }

    private void OnPresentationAttached() {
        ProjectionDocument? document;
        lock (Sync) {
            if (disposed || completed || !started) {
                return;
            }

            document = currentDocument;
        }

        if (document is not null) {
            PublishDocument(document);
        }
    }

    private void OnLifecycleReceived(LifecyclePayload payload) {
        if (payload.InteractionScopeId != Scope.Id) {
            return;
        }

        SurfaceInteractionTermination? termination = null;
        lock (Sync) {
            if (disposed || completed) {
                return;
            }

            if (payload.Phase is not (LifecyclePhase.Completed or LifecyclePhase.Released)) {
                return;
            }

            completed = true;
            termination = new SurfaceInteractionTermination(payload.Phase);
        }

        Terminated?.Invoke(termination.Value);
    }
}
