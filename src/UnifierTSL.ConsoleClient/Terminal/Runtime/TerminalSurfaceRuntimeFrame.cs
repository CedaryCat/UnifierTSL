using UnifierTSL.Contracts.Display;
using UnifierTSL.Contracts.Projection;
using UnifierTSL.Contracts.Terminal;
using UnifierTSL.Contracts.Terminal.Overlay;
using UnifierTSL.Terminal;
using SessionStatusSchema = UnifierTSL.Contracts.Projection.BuiltIn.SessionStatusProjectionSchema;

namespace UnifierTSL.Terminal.Runtime {
    internal enum TerminalSurfaceInteractionMode : byte {
        Editor,
        Display,
    }

    internal sealed class TerminalSurfaceRuntimeFrame {
        public static TerminalSurfaceRuntimeFrame Empty { get; } = new();
        public TerminalSurfaceInteractionMode InteractionMode { get; init; } = TerminalSurfaceInteractionMode.Editor;
        public ProjectionDocument? Document { get; init; }
        public TerminalProjectionRuntimeDocument Projection { get; init; } = TerminalProjectionRuntimeDocument.Empty;
        public long StatusSequence { get; init; }
        public StyleDictionary StyleDictionary { get; init; } = new();
        public EditorPaneRuntimeState EditorPane { get; init; } = new();
        public TerminalAnimatedText InputIndicator { get; init; } = new();
        public TerminalOverlay Overlay { get; init; } = TerminalOverlay.Default;

        public static TerminalSurfaceRuntimeFrame FromSessionStatusDocument(ProjectionDocument document, long statusSequence) {
            if (!SessionStatusSchema.IsSessionStatusScope(document.Scope)) {
                throw new InvalidOperationException($"Unsupported projection status document '{document.Scope.DocumentKind}'.");
            }

            var projection = TerminalProjectionRuntimeDocument.Create(document);
            return new TerminalSurfaceRuntimeFrame {
                InteractionMode = TerminalSurfaceInteractionMode.Display,
                Document = document,
                Projection = projection,
                StatusSequence = statusSequence,
                StyleDictionary = ProjectionStyleDictionaryAdapter.ToDisplayStyleDictionary(
                    document.State.Styles,
                    SurfaceStyleCatalog.Default),
                EditorPane = new EditorPaneRuntimeState {
                    Kind = EditorPaneKind.ReadonlyBuffer,
                    Authority = EditorAuthority.Readonly,
                    AcceptsSubmit = false,
                },
                InputIndicator = projection.ResolveAnimatedText(
                    projection.FindTextByNodeId(SessionStatusSchema.IndicatorNodeId)),
            };
        }

        public static TerminalSurfaceRuntimeFrame FromInteractionDocument(ProjectionDocument document, long statusSequence) {
            if (document.Scope.Kind != ProjectionScopeKind.Interaction) {
                throw new InvalidOperationException($"Interaction runtime frame only accepts interaction documents. Actual: {document.Scope.Kind}.");
            }

            var projection = TerminalProjectionRuntimeDocument.Create(document);
            return new TerminalSurfaceRuntimeFrame {
                InteractionMode = projection.InteractionMode,
                Document = document,
                Projection = projection,
                StatusSequence = statusSequence,
                StyleDictionary = ProjectionStyleDictionaryAdapter.ToDisplayStyleDictionary(
                    document.State.Styles,
                    SurfaceStyleCatalog.Default),
                EditorPane = projection.CreateEditorPane(),
                InputIndicator = projection.ResolveAnimatedText(
                    projection.FindTextBySemanticKey(EditorProjectionSemanticKeys.InputIndicator)),
                Overlay = EditorProjectionDefaultOverlay.Resolve(
                    projection.InteractionMode == TerminalSurfaceInteractionMode.Editor
                    && projection.UsesPopupAssist(),
                    projection.SupportsInteractionStatus()),
            };
        }
    }
}
