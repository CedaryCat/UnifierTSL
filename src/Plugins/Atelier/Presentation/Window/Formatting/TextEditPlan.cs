using Atelier.Session;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Atelier.Presentation.Window.Formatting {
    internal readonly record struct TextEditPlan(
        string Text,
        int CaretIndex,
        ImmutableArray<SourceEdit> Edits) {
        public static TextEditPlan NoChange(string text, int caretIndex) {
            var source = text ?? string.Empty;
            return new TextEditPlan(source, Math.Clamp(caretIndex, 0, source.Length), []);
        }

        public static TextEditPlan FromEdits(
            string text,
            int caretIndex,
            IEnumerable<SourceEdit> edits) {
            var source = text ?? string.Empty;
            var normalizedEdits = DraftMarkers.NormalizeSourceEdits(edits);
            var currentText = DraftMarkers.ApplySourceEditsText(source, normalizedEdits);
            var currentCaret = Math.Clamp(caretIndex, 0, source.Length);
            foreach (var edit in normalizedEdits.OrderByDescending(static edit => edit.StartIndex)) {
                if (edit.StartIndex < currentCaret) {
                    currentCaret = currentCaret <= edit.EndIndex
                        ? edit.StartIndex + edit.InsertedLength
                        : currentCaret + edit.Delta;
                }
            }

            return new TextEditPlan(
                currentText,
                Math.Clamp(currentCaret, 0, currentText.Length),
                normalizedEdits);
        }

        public static ImmutableArray<SourceEdit> ToSourceEdits(IEnumerable<TextChange> changes) {
            return DraftMarkers.NormalizeSourceEdits(changes.Select(static change => new SourceEdit(
                change.Span.Start,
                change.Span.Length,
                change.NewText ?? string.Empty)));
        }

        public DraftRewrite ApplyTo(DraftRewrite rewrite) {
            return rewrite.ApplyBatch(Edits, CaretIndex);
        }
    }
}
