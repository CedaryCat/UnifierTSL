using Atelier.Session;
using UnifierTSL.Surface.Prompting.Model;

namespace Atelier.Presentation.Window.Highlighting {
    internal static class HighlightProjection {
        public static PromptHighlightSpan[] InheritSourceHighlights(
            IReadOnlyList<PromptHighlightSpan>? sourceHighlights,
            IEnumerable<SourceEditBatch>? editBatches) {
            var current = Normalize(sourceHighlights);
            foreach (var batch in editBatches ?? []) {
                if (batch.IsEmpty || current.Length == 0) {
                    continue;
                }

                List<PromptHighlightSpan> next = [];
                foreach (var highlight in current) {
                    if (DraftMarkers.TryMapSourceSpanThroughSourceEdits(
                            highlight.StartIndex,
                            highlight.Length,
                            batch.Edits,
                            out var start,
                            out var length)) {
                        next.Add(new PromptHighlightSpan(start, length, highlight.StyleId));
                    }
                }

                current = [.. next
                    .OrderBy(static span => span.StartIndex)
                    .ThenBy(static span => span.Length)];
            }

            return current;
        }

        public static PromptHighlightSpan[] ProjectSourceHighlights(
            IReadOnlyList<PromptHighlightSpan>? sourceHighlights,
            DraftSnapshot? draft) {
            if (draft is null) {
                return [];
            }

            List<PromptHighlightSpan> projected = [];
            foreach (var highlight in Normalize(sourceHighlights)) {
                if (DraftMarkers.TryMapSourceSpan(
                        draft.SourceMarkers,
                        draft.SourceText.Length,
                        highlight.StartIndex,
                        highlight.Length,
                        out var start,
                        out var length)
                    && length > 0) {
                    projected.Add(new PromptHighlightSpan(start, length, highlight.StyleId));
                }
            }

            return [.. projected
                .OrderBy(static span => span.StartIndex)
                .ThenBy(static span => span.Length)];
        }

        private static PromptHighlightSpan[] Normalize(IReadOnlyList<PromptHighlightSpan>? highlights) {
            return [.. (highlights ?? [])
                .Where(static highlight => highlight.Length > 0 && !string.IsNullOrWhiteSpace(highlight.StyleId))
                .OrderBy(static highlight => highlight.StartIndex)
                .ThenBy(static highlight => highlight.Length)];
        }
    }
}
