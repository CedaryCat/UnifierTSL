using UnifierTSL.Contracts.Display;
using UnifierTSL.Contracts.Projection;
using SessionStatusSchema = UnifierTSL.Contracts.Projection.BuiltIn.SessionStatusProjectionSchema;

namespace UnifierTSL.Terminal.Runtime {
    internal static class TerminalProjectionStatusAdapter {
        public static StyledTextLine[] ResolveInteractionFieldLines(TerminalSurfaceRuntimeFrame frame, string fieldKey) {
            return fieldKey switch {
                SurfaceStatusFieldKeys.Title => ProjectionStyledTextAdapter.ToStyledLines(FindInteractionTitle(frame)?.State.Content),
                SurfaceStatusFieldKeys.Summary => ProjectionStyledTextAdapter.ToStyledLines(FindInteractionSummary(frame)?.State.Content),
                SurfaceStatusFieldKeys.Detail => ProjectionStyledTextAdapter.ToStyledLines(FindInteractionDetail(frame)?.State.Lines),
                _ => [],
            };
        }

        public static StyledTextLine ResolveSessionTitleLine(TerminalSurfaceRuntimeFrame frame) {
            return ProjectionStyledTextAdapter.ToStyledFirstLine(frame.Projection.FindTextByNodeId(SessionStatusSchema.TitleNodeId)?.State.Content);
        }

        public static StyledTextLine ResolveSessionHeaderLine(TerminalSurfaceRuntimeFrame frame) {
            return ProjectionStyledTextAdapter.ToStyledFirstLine(frame.Projection.FindTextByNodeId(SessionStatusSchema.HeaderNodeId)?.State.Content);
        }

        public static StyledTextLine ResolveSessionCompactLine(TerminalSurfaceRuntimeFrame frame) {
            return ProjectionStyledTextAdapter.ToStyledFirstLine(frame.Projection.FindTextByNodeId(SessionStatusSchema.CompactNodeId)?.State.Content);
        }

        public static StyledTextLine[] ResolveSessionDetailLines(TerminalSurfaceRuntimeFrame frame) {
            return ProjectionStyledTextAdapter.ToStyledLines(frame.Projection.FindDetailByNodeId(SessionStatusSchema.DetailNodeId)?.State.Lines);
        }

        public static bool HasVisibleSessionContent(TerminalSurfaceRuntimeFrame frame) {
            if (!frame.Projection.IsContainerVisible(SessionStatusSchema.RootNodeId)) {
                return false;
            }

            return StyledTextLineOps.HasVisibleText(ResolveSessionTitleLine(frame))
                || StyledTextLineOps.HasVisibleText(ResolveSessionHeaderLine(frame))
                || StyledTextLineOps.HasVisibleText(ResolveSessionCompactLine(frame))
                || ResolveSessionDetailLines(frame).Any(StyledTextLineOps.HasVisibleText)
                || frame.InputIndicator.HasVisibleContent;
        }

        private static ProjectionTextNodeRuntime? FindInteractionTitle(TerminalSurfaceRuntimeFrame frame) {
            return frame.Projection.FindTextBySemanticKey(EditorProjectionSemanticKeys.StatusHeader);
        }

        private static ProjectionTextNodeRuntime? FindInteractionSummary(TerminalSurfaceRuntimeFrame frame) {
            return frame.Projection.FindTextBySemanticKey(EditorProjectionSemanticKeys.StatusSummary);
        }

        private static ProjectionDetailNodeRuntime? FindInteractionDetail(TerminalSurfaceRuntimeFrame frame) {
            return frame.Projection.FindDetailBySemanticKey(EditorProjectionSemanticKeys.StatusDetail);
        }
    }
}
