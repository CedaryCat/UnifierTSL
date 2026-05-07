using UnifierTSL.Contracts.Display;
using UnifierTSL.Contracts.Projection;

namespace UnifierTSL.Terminal.Runtime {
    internal static class TerminalProjectionInputAdapter {
        public static InlineSegments ResolvePrompt(TerminalSurfaceRuntimeFrame frame) {
            var input = frame.Projection.FindInput();
            var label = input is { } inputValue
                ? frame.Projection.FindInputLabel(inputValue.Definition.NodeId)
                : null;
            return ProjectionStyledTextAdapter.ToInlineSegments(label?.State.Content);
        }

        public static InlineSegments ResolveContent(TerminalSurfaceRuntimeFrame frame) {
            var input = frame.Projection.FindInput();
            if (input is not { } inputValue) {
                return new InlineSegments {
                    Text = frame.EditorPane.BufferText ?? string.Empty,
                };
            }

            return CreateInputContent(inputValue.State);
        }

        public static GhostInlineHint ResolveGhostHint(TerminalSurfaceRuntimeFrame frame) {
            var input = frame.Projection.FindInput();
            return input is not { } inputValue
                ? new GhostInlineHint()
                : CreateGhostHint(frame, inputValue.State);
        }

        public static EditorSubmitBehavior ResolveSubmitBehavior(TerminalSurfaceRuntimeFrame frame) {
            var submit = frame.Projection.FindInput()?.State.Submit ?? new ProjectionSubmitState();
            return new EditorSubmitBehavior {
                EmptyInputAction = submit.EmptyInputAction == ProjectionEmptySubmitAction.AcceptPreviewIfAvailable
                    ? EmptyInputSubmitAction.AcceptPreviewIfAvailable
                    : EmptyInputSubmitAction.KeepInput,
                CtrlEnterBypassesPreview = submit.AlternateSubmitBypassesPreview,
                PlainEnterReadiness = !submit.AcceptsSubmit
                    ? SubmitReadiness.NotReady
                    : submit.IsReady
                        ? SubmitReadiness.Ready
                        : string.IsNullOrWhiteSpace(submit.Reason)
                            ? SubmitReadiness.UseFallback
                            : SubmitReadiness.NotReady,
            };
        }

        private static InlineSegments CreateInputContent(EditableTextNodeState state) {
            var bufferText = state.BufferText ?? string.Empty;
            return new InlineSegments {
                Text = bufferText,
                Highlights = [.. (state.Decorations ?? [])
                    .Where(static decoration => decoration.Kind == ProjectionInlineDecorationKind.Highlight && decoration.Length > 0)
                    .Select(decoration => new HighlightSpan {
                        StartIndex = Math.Clamp(decoration.StartIndex, 0, bufferText.Length),
                        Length = Math.Clamp(decoration.Length, 0, Math.Max(0, bufferText.Length - Math.Clamp(decoration.StartIndex, 0, bufferText.Length))),
                        StyleId = decoration.Style?.Key ?? string.Empty,
                    })
                    .Where(static highlight => highlight.Length > 0 && !string.IsNullOrEmpty(highlight.StyleId))],
            };
        }

        private static GhostInlineHint CreateGhostHint(
            TerminalSurfaceRuntimeFrame frame,
            EditableTextNodeState state) {
            var selectionState = frame.Projection.FindSelection(
                TerminalProjectionAssistAdapter.FindCompletion(frame)?.Definition.NodeId ?? string.Empty);
            var ghostDecoration = (state.Decorations ?? [])
                .FirstOrDefault(static decoration => decoration.Kind == ProjectionInlineDecorationKind.GhostText);
            return new GhostInlineHint {
                SourceCompletionId = selectionState?.ActiveItemId ?? string.Empty,
                Insertions = [.. (state.Decorations ?? [])
                    .Where(static decoration => decoration.Kind == ProjectionInlineDecorationKind.GhostText)
                    .Select(decoration => new InlineTextInsertion {
                        SourceIndex = Math.Max(0, decoration.StartIndex),
                        Content = ProjectionStyledTextAdapter.ToInlineSegments(decoration.Content),
                    })
                    .Where(insertion => InlineSegmentsOps.HasVisibleText(insertion.Content))],
            };
        }
    }
}
