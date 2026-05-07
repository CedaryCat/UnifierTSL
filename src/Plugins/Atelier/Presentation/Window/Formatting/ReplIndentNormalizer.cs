using Atelier.Session;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;

namespace Atelier.Presentation.Window.Formatting {
    internal static class ReplIndentNormalizer {
        private readonly record struct IndentToken(int StartIndex, SyntaxKind Kind);

        public static DraftRewrite Normalize(
            DraftRewrite rewrite,
            string indentUnit,
            CSharpParseOptions parseOptions) {
            var plan = CreatePlan(
                rewrite.Draft.SourceText,
                rewrite.Draft.SourceCaretIndex,
                string.IsNullOrEmpty(indentUnit) ? "    " : indentUnit,
                parseOptions);
            return plan.ApplyTo(rewrite);
        }

        private static TextEditPlan CreatePlan(
            string text,
            int caretIndex,
            string indentUnit,
            CSharpParseOptions parseOptions) {
            var source = text ?? string.Empty;
            if (source.Length == 0) {
                return TextEditPlan.NoChange(source, 0);
            }

            var tokens = CreateIndentTokens(source, parseOptions);
            var edits = new List<SourceEdit>();
            var tokenIndex = 0;
            var depth = 0;
            var switchLabelExtraDepth = 0;
            for (var lineStart = 0; lineStart < source.Length;) {
                while (tokenIndex < tokens.Length && tokens[tokenIndex].StartIndex < lineStart) {
                    depth = ApplyToken(depth, tokens[tokenIndex++].Kind);
                }

                var lineEnd = GetLineContentEnd(source, lineStart);
                var nextLineStart = GetNextLineStart(source, lineEnd);
                var indentEnd = GetHorizontalWhitespaceEnd(source, lineStart, lineEnd);
                var hasContent = indentEnd < lineEnd;
                if (!hasContent) {
                    if (indentEnd > lineStart) {
                        edits.Add(new SourceEdit(lineStart, indentEnd - lineStart, string.Empty));
                    }
                }
                else {
                    var startsWithCloser = source[indentEnd] is ')' or ']' or '}';
                    var startsWithDirective = source[indentEnd] == '#';
                    var startsWithSwitchLabel = StartsWithSwitchLabel(source, indentEnd, lineEnd);
                    var switchLabelDepth = startsWithCloser || startsWithSwitchLabel ? 0 : switchLabelExtraDepth;
                    var desiredDepth = startsWithDirective
                        ? 0
                        : Math.Max(0, depth - (startsWithCloser ? 1 : 0) + switchLabelDepth);
                    var desiredIndent = string.Concat(Enumerable.Repeat(indentUnit, desiredDepth));
                    if (indentEnd - lineStart != desiredIndent.Length
                        || string.CompareOrdinal(source, lineStart, desiredIndent, 0, desiredIndent.Length) != 0) {
                        edits.Add(new SourceEdit(lineStart, indentEnd - lineStart, desiredIndent));
                    }

                    if (startsWithCloser) {
                        switchLabelExtraDepth = 0;
                    }
                }

                var lineTokenStart = tokenIndex;
                while (tokenIndex < tokens.Length && tokens[tokenIndex].StartIndex < nextLineStart) {
                    depth = ApplyToken(depth, tokens[tokenIndex++].Kind);
                }

                if (hasContent && StartsWithSwitchLabel(source, indentEnd, lineEnd)) {
                    switchLabelExtraDepth = LineContainsOpenBrace(tokens, lineTokenStart, tokenIndex) ? 0 : 1;
                }

                lineStart = nextLineStart;
            }

            return TextEditPlan.FromEdits(source, caretIndex, edits);
        }

        private static ImmutableArray<IndentToken> CreateIndentTokens(string source, CSharpParseOptions parseOptions) {
            try {
                var root = CSharpSyntaxTree.ParseText(source, parseOptions).GetRoot();
                return [.. root.DescendantTokens()
                    .Where(static token => !token.IsMissing && token.Span.Length > 0 && IsIndentToken(token.Kind()))
                    .Select(static token => new IndentToken(token.SpanStart, token.Kind()))
                    .OrderBy(static token => token.StartIndex)];
            }
            catch {
                return [];
            }
        }

        private static int ApplyToken(int depth, SyntaxKind kind) {
            return kind switch {
                SyntaxKind.CloseParenToken or SyntaxKind.CloseBracketToken or SyntaxKind.CloseBraceToken
                    => Math.Max(0, depth - 1),
                SyntaxKind.OpenParenToken or SyntaxKind.OpenBracketToken or SyntaxKind.OpenBraceToken => depth + 1,
                _ => depth,
            };
        }

        private static bool IsIndentToken(SyntaxKind kind) {
            return kind is SyntaxKind.OpenParenToken
                or SyntaxKind.CloseParenToken
                or SyntaxKind.OpenBracketToken
                or SyntaxKind.CloseBracketToken
                or SyntaxKind.OpenBraceToken
                or SyntaxKind.CloseBraceToken;
        }

        private static bool StartsWithSwitchLabel(string text, int startIndex, int lineEnd) {
            return StartsWithWord(text, startIndex, lineEnd, "case")
                || StartsWithWord(text, startIndex, lineEnd, "default");
        }

        private static bool StartsWithWord(string text, int startIndex, int lineEnd, string word) {
            if (startIndex + word.Length > lineEnd
                || !text.AsSpan(startIndex, word.Length).Equals(word.AsSpan(), StringComparison.Ordinal)) {
                return false;
            }

            return startIndex + word.Length == lineEnd
                || !IsIdentifierPart(text[startIndex + word.Length]);
        }

        private static bool IsIdentifierPart(char value) {
            return char.IsLetterOrDigit(value) || value == '_';
        }

        private static bool LineContainsOpenBrace(
            ImmutableArray<IndentToken> tokens,
            int startTokenIndex,
            int endTokenIndex) {

            for (var index = startTokenIndex; index < endTokenIndex; index++) {
                if (tokens[index].Kind == SyntaxKind.OpenBraceToken) {
                    return true;
                }
            }

            return false;
        }

        private static int GetLineContentEnd(string text, int lineStart) {
            for (var index = lineStart; index < text.Length; index++) {
                if (text[index] is '\r' or '\n') {
                    return index;
                }
            }

            return text.Length;
        }

        private static int GetNextLineStart(string text, int lineEnd) {
            if (lineEnd >= text.Length) {
                return text.Length;
            }

            return text[lineEnd] == '\r' && lineEnd + 1 < text.Length && text[lineEnd + 1] == '\n'
                ? lineEnd + 2
                : lineEnd + 1;
        }

        private static int GetHorizontalWhitespaceEnd(string text, int startIndex, int endIndex) {
            var index = Math.Clamp(startIndex, 0, text.Length);
            var end = Math.Clamp(endIndex, index, text.Length);
            while (index < end && text[index] is ' ' or '\t') {
                index++;
            }

            return index;
        }
    }
}
