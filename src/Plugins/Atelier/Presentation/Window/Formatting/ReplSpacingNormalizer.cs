using Atelier.Session;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using UnifierTSL.TextEditing;

namespace Atelier.Presentation.Window.Formatting {
    internal static class ReplSpacingNormalizer {
        public static DraftRewrite NormalizeGenericAngleSpacing(
            DraftRewrite rewrite,
            CSharpParseOptions parseOptions) {
            var normalized = CreateGenericAngleSpacingPlan(
                rewrite.Draft.SourceText,
                rewrite.Draft.SourceCaretIndex,
                parseOptions);
            return string.Equals(normalized.Text, rewrite.Draft.SourceText, StringComparison.Ordinal)
                ? rewrite
                : normalized.ApplyTo(rewrite);
        }

        private static TextEditPlan CreateGenericAngleSpacingPlan(
            string text,
            int caretIndex,
            CSharpParseOptions parseOptions) {
            var source = text ?? string.Empty;
            if (source.Length == 0) {
                return TextEditPlan.NoChange(source, 0);
            }

            var lineStart = LogicalLineBounds.GetStartIndex(source, caretIndex);
            var lineEnd = LogicalLineBounds.GetContentEndIndex(source, lineStart);
            var lineSpan = TextSpan.FromBounds(lineStart, lineEnd);
            var root = CSharpSyntaxTree.ParseText(source, parseOptions).GetRoot();
            List<SourceEdit> edits = [];
            foreach (var list in root.DescendantNodes().Where(node => node.Span.IntersectsWith(lineSpan))) {
                switch (list) {
                    case TypeArgumentListSyntax typeArgs:
                        AppendTypeArgumentSpacingEdits(edits, source, typeArgs);
                        break;
                    case TypeParameterListSyntax typeParams:
                        AppendTypeParameterSpacingEdits(edits, source, typeParams);
                        break;
                }
            }

            return TextEditPlan.FromEdits(source, caretIndex, edits);
        }

        private static void AppendTypeArgumentSpacingEdits(
            List<SourceEdit> edits,
            string text,
            TypeArgumentListSyntax list) {
            if (list.LessThanToken.IsMissing || list.GreaterThanToken.IsMissing || list.Arguments.Count == 0) {
                return;
            }

            AppendHorizontalGapEdit(
                edits,
                text,
                list.LessThanToken.Span.End,
                list.Arguments[0].SpanStart,
                string.Empty);
            AppendHorizontalGapEdit(
                edits,
                text,
                list.Arguments[^1].Span.End,
                list.GreaterThanToken.SpanStart,
                string.Empty);
            AppendSeparatedListSpacingEdits(edits, text, list.Arguments);
        }

        private static void AppendTypeParameterSpacingEdits(
            List<SourceEdit> edits,
            string text,
            TypeParameterListSyntax list) {
            if (list.LessThanToken.IsMissing || list.GreaterThanToken.IsMissing || list.Parameters.Count == 0) {
                return;
            }

            AppendHorizontalGapEdit(
                edits,
                text,
                list.LessThanToken.Span.End,
                list.Parameters[0].SpanStart,
                string.Empty);
            AppendHorizontalGapEdit(
                edits,
                text,
                list.Parameters[^1].Span.End,
                list.GreaterThanToken.SpanStart,
                string.Empty);
            AppendSeparatedListSpacingEdits(edits, text, list.Parameters);
        }

        private static void AppendSeparatedListSpacingEdits<TNode>(
            List<SourceEdit> edits,
            string text,
            SeparatedSyntaxList<TNode> list)
            where TNode : SyntaxNode {
            for (var index = 0; index < list.SeparatorCount; index++) {
                var separator = list.GetSeparator(index);
                if (separator.IsMissing || !separator.IsKind(SyntaxKind.CommaToken) || index + 1 >= list.Count) {
                    continue;
                }

                AppendHorizontalGapEdit(edits, text, list[index].Span.End, separator.SpanStart, string.Empty);
                AppendHorizontalGapEdit(edits, text, separator.Span.End, list[index + 1].SpanStart, " ");
            }
        }

        private static void AppendHorizontalGapEdit(
            List<SourceEdit> edits,
            string text,
            int startIndex,
            int endIndex,
            string replacement) {
            var start = Math.Clamp(startIndex, 0, text.Length);
            var end = Math.Clamp(endIndex, start, text.Length);
            if (text.AsSpan(start, end - start).IndexOfAnyExcept(' ', '\t') >= 0) {
                return;
            }

            if (end - start == replacement.Length
                && string.Equals(text.Substring(start, end - start), replacement, StringComparison.Ordinal)) {
                return;
            }

            edits.Add(new SourceEdit(start, end - start, replacement));
        }
    }
}
