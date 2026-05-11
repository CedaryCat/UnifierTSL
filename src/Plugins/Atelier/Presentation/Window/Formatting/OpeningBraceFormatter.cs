using Atelier.Session;
using Microsoft.CodeAnalysis.CSharp;

namespace Atelier.Presentation.Window.Formatting {
    internal static class OpeningBraceFormatter {
        public static DraftRewrite? TryRewrite(
            DraftRewrite rewrite,
            int braceIndex,
            CSharpParseOptions parseOptions) {
            var draft = rewrite.Draft;
            if (braceIndex < 0 || braceIndex >= draft.SourceText.Length || draft.SourceText[braceIndex] != '{') {
                return null;
            }

            return ApplyVirtualCloserSpacing(
                TypedCharacterFormatter.TryFormat(rewrite, '{', braceIndex, parseOptions) ?? rewrite);
        }

        private static DraftRewrite ApplyVirtualCloserSpacing(DraftRewrite rewrite) {
            var draft = rewrite.Draft;
            var entry = draft.PairLedger.Entries
                .Where(entry => entry.Kind == VirtualPairKind.Brace
                    && entry.OpenerIndex < draft.SourceCaretIndex
                    && draft.SourceCaretIndex <= entry.CloserIndex
                    && string.Equals(entry.MarkerKey, DraftMarkers.CloseBraceKey, StringComparison.Ordinal))
                .OrderByDescending(static entry => entry.OpenerIndex)
                .FirstOrDefault();
            if (entry is null) {
                return rewrite;
            }

            var nextIndex = entry.CloserIndex + entry.CloserLength;
            return nextIndex < draft.SourceText.Length && draft.SourceText[nextIndex] == '}'
                ? rewrite.Apply(new SourceEdit(nextIndex, 0, " "), draft.SourceCaretIndex)
                : rewrite;
        }
    }
}
