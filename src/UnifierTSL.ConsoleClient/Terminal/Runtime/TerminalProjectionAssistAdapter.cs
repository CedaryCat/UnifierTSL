using UnifierTSL.Contracts.Display;
using UnifierTSL.Contracts.Projection;

namespace UnifierTSL.Terminal.Runtime {
    internal static class TerminalProjectionAssistAdapter {
        public static ProjectionCollectionNodeRuntime? FindCompletion(TerminalSurfaceRuntimeFrame frame) {
            return frame.Projection.FindCollectionBySemanticKey(EditorProjectionSemanticKeys.AssistPrimaryList);
        }

        public static ProjectionDetailNodeRuntime? FindCompletionDetail(TerminalSurfaceRuntimeFrame frame) {
            var completion = FindCompletion(frame);
            return frame.Projection.FindDetailBySemanticKey(EditorProjectionSemanticKeys.AssistPrimaryDetail)
                ?? (completion is { } completionValue
                    ? frame.Projection.FindBoundDetail(completionValue.Definition.NodeId)
                    : null);
        }

        public static ProjectionPropertySetNodeRuntime? FindCompletionMetadata(TerminalSurfaceRuntimeFrame frame) {
            return frame.Projection.FindPropertySetBySemanticKey(EditorProjectionSemanticKeys.AssistPrimaryMetadata);
        }

        public static ProjectionCollectionNodeRuntime? FindInterpretation(TerminalSurfaceRuntimeFrame frame) {
            return frame.Projection.FindCollectionBySemanticKey(EditorProjectionSemanticKeys.AssistSecondaryList);
        }

        public static ProjectionTextNodeRuntime? FindInterpretationSummary(TerminalSurfaceRuntimeFrame frame) {
            return frame.Projection.FindText(
                EditorProjectionSemanticKeys.AssistSecondarySummary,
                static node => node.Role == ProjectionSemanticRole.Summary);
        }

        public static ProjectionDetailNodeRuntime? FindInterpretationDetail(TerminalSurfaceRuntimeFrame frame) {
            var interpretation = FindInterpretation(frame);
            return frame.Projection.FindDetailBySemanticKey(EditorProjectionSemanticKeys.AssistSecondaryDetail)
                ?? (interpretation is { } interpretationValue
                    ? frame.Projection.FindBoundDetail(interpretationValue.Definition.NodeId)
                    : null);
        }

        public static bool UsesExpandedInterpretationPopupLayout(TerminalSurfaceRuntimeFrame frame) {
            return FindInterpretationDetail(frame) is {
                Definition.Traits.ExpansionHint: ProjectionExpansionHint.Expanded
            };
        }

        public static CompletionActivationMode ResolveCompletionActivationMode(TerminalSurfaceRuntimeFrame frame) {
            var rawValue = frame.Projection.ResolveMetadataValue(
                FindCompletionMetadata(frame),
                EditorProjectionMetadataKeys.AssistPrimaryActivationMode);
            return rawValue switch {
                "automatic" => CompletionActivationMode.Automatic,
                "manual" => CompletionActivationMode.Manual,
                _ => frame.EditorPane.AuthoringBehavior.OpensCompletionAutomatically
                    ? CompletionActivationMode.Automatic
                    : CompletionActivationMode.Manual,
            };
        }

        public static string ResolveActiveItemId(
            TerminalSurfaceRuntimeFrame frame,
            ProjectionCollectionNodeRuntime? collection,
            ProjectionDetailNodeRuntime? detail) {
            return frame.Projection.ResolveActiveItemId(collection, detail);
        }

        public static int ResolveSelectedItemIndex(
            TerminalSurfaceRuntimeFrame frame,
            ProjectionCollectionNodeRuntime? collection,
            ProjectionDetailNodeRuntime? detail) {
            return frame.Projection.ResolveItemIndex(collection?.State.Items, ResolveActiveItemId(frame, collection, detail));
        }

        public static TextEditOperation ResolvePrimaryEdit(TerminalSurfaceRuntimeFrame frame, ProjectionCollectionItem item) {
            var inputNodeId = frame.Projection.FindInput()?.Definition.NodeId ?? string.Empty;
            return string.Equals(item.PrimaryEdit.TargetNodeId, inputNodeId, StringComparison.Ordinal)
                ? new TextEditOperation {
                    StartIndex = item.PrimaryEdit.StartIndex,
                    Length = item.PrimaryEdit.Length,
                    NewText = item.PrimaryEdit.NewText,
                }
                : new TextEditOperation();
        }
    }
}
