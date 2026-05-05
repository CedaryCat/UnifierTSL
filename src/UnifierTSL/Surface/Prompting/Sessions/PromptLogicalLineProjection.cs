using UnifierTSL.Surface.Prompting;
using UnifierTSL.Surface.Prompting.Model;
using UnifierTSL.Surface.Prompting.Runtime;
using UnifierTSL.TextEditing;

namespace UnifierTSL.Surface.Prompting.Sessions;
    internal readonly record struct PromptLogicalLineProjection(
        string BufferText,
        int LineStartIndex,
        int LineContentEndIndex)
    {
        public static PromptLogicalLineProjection Create(string? bufferText, int caretIndex) {
            var text = bufferText ?? string.Empty;
            var boundedCaret = Math.Clamp(caretIndex, 0, text.Length);
            var lineStartIndex = LogicalLineBounds.GetStartIndex(text, boundedCaret);
            var lineContentEndIndex = LogicalLineBounds.GetContentEndIndex(text, boundedCaret);
            return new PromptLogicalLineProjection(
                text,
                lineStartIndex,
                lineContentEndIndex);
        }

        public PromptInputState Project(PromptInputState state) {
            var projectedState = state.CopyNormalized();
            projectedState.InputText = BufferText[LineStartIndex..LineContentEndIndex];
            projectedState.CursorIndex = state.CursorIndex - LineStartIndex;
            projectedState.PreferredCompletionText = ProjectDocumentText(state.PreferredCompletionText);
            return projectedState.Normalize();
        }

        public PromptInteractionState Rebase(PromptInteractionState state) {
            var inputState = Rebase(state.InputState);
            return new PromptInteractionState(
                state.Purpose,
                inputState,
                state.Content,
                Rebase(state.Computation),
                state.CandidateWindow);
        }

        public PromptHighlightSpan[] RebaseInputHighlights(IEnumerable<PromptHighlightSpan> highlights) {
            return [.. highlights.Select(Rebase)];
        }

        private PromptInputState Rebase(PromptInputState state) {
            var rebasedState = state.CopyNormalized();
            rebasedState.InputText = BufferText;
            rebasedState.CursorIndex = LineStartIndex + state.CursorIndex;
            rebasedState.PreferredCompletionText = RebaseLineText(state.PreferredCompletionText);
            return rebasedState.Normalize();
        }

        private PromptComputation Rebase(PromptComputation computation) {
            return computation with {
                InputHighlights = [.. computation.InputHighlights.Select(Rebase)],
                Suggestions = [.. computation.Suggestions.Select(Rebase)],
                PreferredCompletionText = RebaseLineText(computation.PreferredCompletionText),
            };
        }

        private PromptHighlightSpan Rebase(PromptHighlightSpan highlight) {
            return new PromptHighlightSpan(
                LineStartIndex + Math.Max(0, highlight.StartIndex),
                Math.Max(0, highlight.Length),
                highlight.StyleId);
        }

        private PromptCompletionItem Rebase(PromptCompletionItem item) {
            return new PromptCompletionItem {
                Id = item.Id ?? string.Empty,
                DisplayText = item.DisplayText ?? string.Empty,
                SecondaryDisplayText = item.SecondaryDisplayText ?? string.Empty,
                TrailingDisplayText = item.TrailingDisplayText ?? string.Empty,
                SummaryText = item.SummaryText ?? string.Empty,
                DisplayStyleId = item.DisplayStyleId ?? string.Empty,
                SecondaryDisplayStyleId = item.SecondaryDisplayStyleId ?? string.Empty,
                TrailingDisplayStyleId = item.TrailingDisplayStyleId ?? string.Empty,
                SummaryStyleId = item.SummaryStyleId ?? string.Empty,
                PreviewStyleId = item.PreviewStyleId ?? string.Empty,
                DisplayHighlights = [.. item.DisplayHighlights.Select(Rebase)],
                SecondaryDisplayHighlights = [.. item.SecondaryDisplayHighlights.Select(Rebase)],
                TrailingDisplayHighlights = [.. item.TrailingDisplayHighlights.Select(Rebase)],
                SummaryHighlights = [.. item.SummaryHighlights.Select(Rebase)],
                Weight = item.Weight,
                PrimaryEdit = Rebase(item.PrimaryEdit),
            };
        }

        private PromptTextEdit Rebase(PromptTextEdit edit) {
            return new PromptTextEdit(
                LineStartIndex + Math.Max(0, edit.StartIndex),
                Math.Max(0, edit.Length),
                edit.NewText ?? string.Empty);
        }

        private string ProjectDocumentText(string? documentText) {
            var value = documentText ?? string.Empty;
            if (value.Length == 0) {
                return string.Empty;
            }

            var prefix = BufferText[..LineStartIndex];
            var suffix = BufferText[LineContentEndIndex..];
            return value.StartsWith(prefix, StringComparison.Ordinal)
                && value.EndsWith(suffix, StringComparison.Ordinal)
                && value.Length >= prefix.Length + suffix.Length
                    ? value[prefix.Length..(value.Length - suffix.Length)]
                    : string.Empty;
        }

        private string RebaseLineText(string? lineText) {
            var value = lineText ?? string.Empty;
            if (value.Length == 0) {
                return string.Empty;
            }

            return BufferText[..LineStartIndex] + value + BufferText[LineContentEndIndex..];
        }
    }
