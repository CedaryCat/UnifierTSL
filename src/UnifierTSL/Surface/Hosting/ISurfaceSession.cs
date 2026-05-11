using UnifierTSL.Contracts.Projection;
using UnifierTSL.Contracts.Protocol.Payloads;
using UnifierTSL.Contracts.Sessions;

namespace UnifierTSL.Surface.Hosting {
    public interface ISurfaceSession : IDisposable {
        event Action? PresentationAttached;
        event Action? PresentationDetached;
        event Action? ReleaseRequested;
        event Action<int>? ActivitySelectionRequested;
        event Action<InputEventPayload>? InputReceived;
        event Action<LifecyclePayload>? LifecycleReceived;
        event Action<SurfaceCompletionPayload>? CompletionReceived;

        bool IsPresentationAttached { get; }

        void Start();
        void PublishSurfaceHostOperation(SurfaceHostOperation operation);
        void PublishProjectionSnapshot(ProjectionSnapshotPayload snapshot);
        void PublishSurfaceOperation(SurfaceOperation operation);
        InteractionScope OpenInteractionScope(
            string interactionKind,
            bool isTransient = true);
    }
}
