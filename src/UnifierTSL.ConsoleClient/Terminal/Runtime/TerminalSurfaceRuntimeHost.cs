using UnifierTSL.Contracts.Projection;
using UnifierTSL.Contracts.Protocol.Payloads;
using UnifierTSL.Contracts.Sessions;

namespace UnifierTSL.Terminal.Runtime {
    internal sealed class TerminalSurfaceRuntimeHost : IDisposable {
        internal readonly record struct ProjectionStatusFrameResult(
            ProjectionDocument Document,
            TerminalSurfaceRuntimeFrame Frame);
        internal readonly record struct ProjectionInteractionFrameResult(
            ProjectionDocument Document,
            TerminalSurfaceRuntimeFrame Frame);

        private readonly Lock stateLock = new();
        private TerminalSurfaceRuntimeBase? surfaceRendererRuntime;

        public void ApplyBootstrap(SurfaceBootstrap bootstrap) {
            GetOrCreateRuntime();
        }

        public ProjectionStatusFrameResult CreateStatusFrame(
            ProjectionSnapshotPayload payload,
            ProjectionDocument? existingDocument) {
            var document = ProjectionDocumentOps.ApplySnapshot(existingDocument, payload);
            return new ProjectionStatusFrameResult(
                document,
                TerminalSurfaceRuntimeFrame.FromSessionStatusDocument(document, payload.Sequences.DocumentSequence));
        }

        public ProjectionInteractionFrameResult CreateInteractionFrame(
            ProjectionSnapshotPayload payload,
            ProjectionDocument? existingDocument) {
            var document = ProjectionDocumentOps.ApplySnapshot(existingDocument, payload);
            return new ProjectionInteractionFrameResult(
                document,
                InteractionProjectionRuntimeFrameCompiler.CreateFrame(document, payload.Sequences.DocumentSequence));
        }

        public TerminalSurfaceInteraction CreateInteraction(
            InteractionScopeId scopeId,
            TerminalSurfaceInteraction? existingInteraction,
            LifecyclePayload? lifecycle,
            TerminalSurfaceRuntimeFrame? frame) {
            return GetOrCreateRuntime().CreateInteraction(scopeId, existingInteraction, lifecycle, frame);
        }

        public void ApplySurfaceHostOperation(SurfaceHostOperation operation) {
            GetOrCreateRuntime().ApplySurfaceHostOperation(operation);
        }

        public void ApplyStream(StreamPayload payload) {
            GetOrCreateRuntime().ApplyStream(payload);
        }

        public void ApplyStatusSnapshot(TerminalSurfaceRuntimeFrame frame) {
            GetOrCreateRuntime().ApplyStatusSnapshot(frame);
        }

        public void ApplyInteractionFrame(TerminalSurfaceInteraction interaction, TerminalSurfaceRuntimeFrame frame) {
            GetOrCreateRuntime().ApplyInteractionFrame(interaction, frame);
        }

        public SurfaceCompletion ExecuteInteraction(
            TerminalSurfaceInteraction interaction,
            TerminalSurfaceRuntimeFrame frame,
            Action<ClientBufferedEditorState> onBufferedEditorState,
            Action<ClientBufferedEditorState> onSubmitBufferedEditorState,
            Action<ConsoleKeyInfo> onKeyPressed,
            Action<int> onActivitySelectionRequested,
            CancellationToken cancellationToken) {
            return GetOrCreateRuntime().ExecuteInteraction(
                interaction,
                frame,
                onBufferedEditorState,
                onSubmitBufferedEditorState,
                onKeyPressed,
                onActivitySelectionRequested,
                cancellationToken);
        }

        public void Dispose() {
            lock (stateLock) {
                surfaceRendererRuntime?.Dispose();
                surfaceRendererRuntime = null;
            }
        }

        private TerminalSurfaceRuntimeBase GetOrCreateRuntime() {
            lock (stateLock) {
                return surfaceRendererRuntime ??= new TerminalSurfaceRuntimeBase();
            }
        }
    }
}
