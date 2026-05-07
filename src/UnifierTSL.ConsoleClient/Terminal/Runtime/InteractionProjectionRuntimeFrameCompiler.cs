using UnifierTSL.Contracts.Projection;

namespace UnifierTSL.Terminal.Runtime {
    internal static class InteractionProjectionRuntimeFrameCompiler {
        public static TerminalSurfaceRuntimeFrame CreateFrame(ProjectionDocument document, long statusSequence) {
            return TerminalSurfaceRuntimeFrame.FromInteractionDocument(document, statusSequence);
        }
    }
}
