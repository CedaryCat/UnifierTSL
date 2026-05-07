using Atelier.Session;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using UnifierTSL.TextEditing;

namespace Atelier.Presentation.Window.Drafting {
    internal readonly record struct BraceLayoutNormalization(
        string Text,
        int BraceIndex,
        ImmutableArray<SourceEdit> Edits);

    internal static class BracePairLayoutNormalizer {
        private static readonly string[] ControlKeywordsWithParens = [
            "if",
            "for",
            "foreach",
            "while",
            "switch",
            "catch",
            "using",
            "lock",
        ];

        public static BraceLayoutNormalization NormalizeOpeningBraceLayout(
            string text,
            int braceIndex,
            CSharpParseOptions parseOptions,
            bool useKAndRBraceStyle) {
            var boundedBraceIndex = Math.Clamp(braceIndex, 0, Math.Max(0, text.Length - 1));
            if (text.Length == 0 || boundedBraceIndex >= text.Length || text[boundedBraceIndex] != '{') {
                return new BraceLayoutNormalization(text, boundedBraceIndex, []);
            }

            var segmentStart = ResolveBraceSegmentStart(text, boundedBraceIndex, parseOptions);
            var segment = text[segmentStart..boundedBraceIndex];
            var leadingWhitespaceLength = 0;
            while (leadingWhitespaceLength < segment.Length && segment[leadingWhitespaceLength] is ' ' or '\t') {
                leadingWhitespaceLength++;
            }

            var leadingWhitespace = segment[..leadingWhitespaceLength];
            var body = segment[leadingWhitespaceLength..].TrimEnd();
            foreach (var keyword in ControlKeywordsWithParens) {
                if (body.StartsWith(keyword + "(", StringComparison.Ordinal)) {
                    body = keyword + " " + body[keyword.Length..];
                    break;
                }
            }

            var normalizedSegment = leadingWhitespace + body;
            if (normalizedSegment.Length == leadingWhitespace.Length) {
                return new BraceLayoutNormalization(text, boundedBraceIndex, []);
            }

            var previousChar = body[^1];
            if (previousChar == '}') {
                normalizedSegment += "\n" + leadingWhitespace;
            }
            else if (useKAndRBraceStyle) {
                if (previousChar is not (')' or '>' or '{')) {
                    return new BraceLayoutNormalization(text, boundedBraceIndex, []);
                }

                if (!char.IsWhiteSpace(normalizedSegment[^1])) {
                    normalizedSegment += " ";
                }
            }
            else {
                normalizedSegment += "\n" + leadingWhitespace;
            }

            if (string.Equals(segment, normalizedSegment, StringComparison.Ordinal)) {
                return new BraceLayoutNormalization(text, boundedBraceIndex, []);
            }

            var normalizedText = text[..segmentStart] + normalizedSegment + text[boundedBraceIndex..];
            return new BraceLayoutNormalization(
                normalizedText,
                segmentStart + normalizedSegment.Length,
                [new SourceEdit(segmentStart, segment.Length, normalizedSegment)]);
        }

        private static int ResolveBraceSegmentStart(string text, int braceIndex, CSharpParseOptions parseOptions) {
            var lineStart = LogicalLineBounds.GetStartIndex(text, braceIndex);
            var root = CSharpSyntaxTree.ParseText(text ?? string.Empty, parseOptions).GetRoot();
            var braceToken = root.FindToken(braceIndex, findInsideTrivia: true);
            if (!braceToken.IsKind(SyntaxKind.OpenBraceToken) || braceToken.SpanStart != braceIndex) {
                return lineStart;
            }

            var segmentStart = lineStart;
            var parenDepth = 0;
            foreach (var token in root.DescendantTokens(descendIntoTrivia: true)) {
                if (token.RawKind == 0 || token.IsMissing || token.SpanStart < lineStart) {
                    continue;
                }

                if (token.SpanStart >= braceIndex) {
                    break;
                }

                switch (token.Kind()) {
                    case SyntaxKind.OpenParenToken:
                        parenDepth++;
                        break;
                    case SyntaxKind.CloseParenToken:
                        parenDepth = Math.Max(0, parenDepth - 1);
                        break;
                    case SyntaxKind.SemicolonToken when parenDepth == 0:
                        segmentStart = token.Span.End;
                        break;
                }
            }

            return segmentStart;
        }
    }
}
