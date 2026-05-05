using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;

namespace Atelier.Session {
    internal enum VirtualPairKind {
        Parenthesis,
        Bracket,
        Brace,
        Angle,
        DoubleQuote,
        SingleQuote,
    }

    internal readonly record struct VirtualPair(
        VirtualPairKind Kind,
        SourceTextMarker Marker,
        long PairId,
        int OpenerIndex,
        int CloserIndex) {
        public int EditableStartIndex => OpenerIndex + 1;
        public int EditableEndIndex => CloserIndex;

        public bool ContainsCaret(int caretIndex) {
            return caretIndex >= EditableStartIndex && caretIndex <= EditableEndIndex;
        }
    }

    internal sealed class VirtualPairAnalysis(
        ImmutableArray<VirtualPair> pairs,
        VirtualPair? activePair)
    {
        public ImmutableArray<VirtualPair> Pairs { get; } = pairs.IsDefault ? [] : pairs;
        public VirtualPair? ActivePair { get; } = activePair;
    }

    internal static class VirtualPairAnalyzer {
        public static VirtualPairAnalysis Analyze(
            DraftSnapshot draft,
            CSharpParseOptions parseOptions) {
            return Analyze(draft.SourceText, draft.SourceMarkers, draft.PairLedger, draft.SourceCaretIndex, parseOptions);
        }

        public static VirtualPairAnalysis Analyze(
            string? sourceText,
            IReadOnlyList<SourceTextMarker>? markers,
            VirtualPairLedger pairLedger,
            int caretIndex,
            CSharpParseOptions parseOptions) {
            var text = sourceText ?? string.Empty;
            var normalizedMarkers = DraftMarkers.NormalizeSourceMarkers(markers, text.Length, pairLedger);
            Dictionary<long, SourceTextMarker> markersByPairId = [];
            foreach (var marker in normalizedMarkers) {
                markersByPairId[marker.PairId] = marker;
            }

            var tokens = text.Length == 0
                ? []
                : CSharpSyntaxTree.ParseText(text, parseOptions).GetRoot().DescendantTokens(descendIntoTrivia: true).ToArray();
            List<VirtualPair> pairs = [];
            foreach (var entry in pairLedger.Entries) {
                if (!markersByPairId.TryGetValue(entry.PairId, out var marker)
                    || !EntryMatchesMarker(entry, marker)
                    || !EntryMatchesSyntax(text, tokens, entry)) {
                    continue;
                }

                pairs.Add(new VirtualPair(entry.Kind, marker, entry.PairId, entry.OpenerIndex, entry.CloserIndex));
            }

            var boundedCaret = Math.Clamp(caretIndex, 0, text.Length);
            var activePair = pairs
                .Where(pair => pair.ContainsCaret(boundedCaret))
                .OrderByDescending(static pair => pair.OpenerIndex)
                .ThenBy(static pair => pair.CloserIndex)
                .Cast<VirtualPair?>()
                .FirstOrDefault();
            return new VirtualPairAnalysis([.. pairs.OrderBy(static pair => pair.OpenerIndex)], activePair);
        }

        public static ImmutableArray<SourceTextMarker> ResolveExitedMarkers(
            DraftSnapshot draft,
            int previousCaretIndex,
            int currentCaretIndex,
            CSharpParseOptions parseOptions) {
            var boundedPreviousCaret = Math.Clamp(previousCaretIndex, 0, draft.SourceText.Length);
            var boundedCurrentCaret = Math.Clamp(currentCaretIndex, 0, draft.SourceText.Length);
            var analysis = Analyze(draft.SourceText, draft.SourceMarkers, draft.PairLedger, boundedPreviousCaret, parseOptions);
            return [.. analysis.Pairs
                .Where(pair => pair.ContainsCaret(boundedPreviousCaret) && !pair.ContainsCaret(boundedCurrentCaret))
                .OrderByDescending(static pair => pair.OpenerIndex)
                .Select(static pair => pair.Marker)];
        }

        private static bool EntryMatchesMarker(VirtualPairLedgerEntry entry, SourceTextMarker marker) {
            return marker.PairId == entry.PairId
                && marker.SourceStartIndex == entry.CloserIndex
                && marker.SourceLength == entry.CloserLength
                && string.Equals(marker.BaseKey, entry.MarkerKey, StringComparison.Ordinal);
        }

        private static bool EntryMatchesSyntax(
            string text,
            IReadOnlyList<SyntaxToken> tokens,
            VirtualPairLedgerEntry entry) {
            if (!TryGetPairChars(entry.Kind, out var opener, out var closer)
                || entry.OpenerIndex < 0
                || entry.OpenerIndex >= text.Length
                || entry.CloserIndex < 0
                || entry.CloserIndex >= text.Length
                || text[entry.OpenerIndex] != opener
                || text[entry.CloserIndex] != closer) {
                return false;
            }

            return entry.Kind switch {
                VirtualPairKind.Parenthesis => HasToken(tokens, SyntaxKind.OpenParenToken, entry.OpenerIndex)
                    && HasToken(tokens, SyntaxKind.CloseParenToken, entry.CloserIndex),
                VirtualPairKind.Bracket => HasToken(tokens, SyntaxKind.OpenBracketToken, entry.OpenerIndex)
                    && HasToken(tokens, SyntaxKind.CloseBracketToken, entry.CloserIndex),
                VirtualPairKind.Brace => HasToken(tokens, SyntaxKind.OpenBraceToken, entry.OpenerIndex)
                    && HasToken(tokens, SyntaxKind.CloseBraceToken, entry.CloserIndex),
                VirtualPairKind.Angle => HasToken(tokens, SyntaxKind.LessThanToken, entry.OpenerIndex)
                    && HasToken(tokens, SyntaxKind.GreaterThanToken, entry.CloserIndex),
                VirtualPairKind.DoubleQuote => HasQuoteToken(tokens, text, entry, SyntaxKind.StringLiteralToken)
                    || HasInterpolatedStringPair(tokens, entry),
                VirtualPairKind.SingleQuote => HasQuoteToken(tokens, text, entry, SyntaxKind.CharacterLiteralToken),
                _ => false,
            };
        }

        private static bool TryGetPairChars(VirtualPairKind kind, out char opener, out char closer) {
            (opener, closer) = kind switch {
                VirtualPairKind.Parenthesis => ('(', ')'),
                VirtualPairKind.Bracket => ('[', ']'),
                VirtualPairKind.Brace => ('{', '}'),
                VirtualPairKind.Angle => ('<', '>'),
                VirtualPairKind.DoubleQuote => ('"', '"'),
                VirtualPairKind.SingleQuote => ('\'', '\''),
                _ => ('\0', '\0'),
            };
            return opener != '\0';
        }

        private static bool HasToken(IReadOnlyList<SyntaxToken> tokens, SyntaxKind kind, int spanStart) {
            return tokens.Any(token => token.IsKind(kind) && token.SpanStart == spanStart && !token.IsMissing);
        }

        private static bool HasQuoteToken(
            IReadOnlyList<SyntaxToken> tokens,
            string text,
            VirtualPairLedgerEntry entry,
            SyntaxKind tokenKind) {
            foreach (var token in tokens) {
                if (!token.IsKind(tokenKind) || token.IsMissing || token.Span.IsEmpty) {
                    continue;
                }

                var quote = entry.Kind == VirtualPairKind.DoubleQuote ? '"' : '\'';
                var openerIndex = text.IndexOf(quote, token.SpanStart, token.Span.Length);
                var closerIndex = text.LastIndexOf(quote, token.Span.End - 1, token.Span.Length);
                if (openerIndex == entry.OpenerIndex && closerIndex == entry.CloserIndex) {
                    return true;
                }
            }

            return false;
        }

        private static bool HasInterpolatedStringPair(IReadOnlyList<SyntaxToken> tokens, VirtualPairLedgerEntry entry) {
            var hasStart = tokens.Any(token => token.IsKind(SyntaxKind.InterpolatedStringStartToken)
                && token.Span.Contains(entry.OpenerIndex)
                && !token.IsMissing);
            var hasEnd = tokens.Any(token => token.IsKind(SyntaxKind.InterpolatedStringEndToken)
                && token.SpanStart == entry.CloserIndex
                && !token.IsMissing);
            return hasStart && hasEnd;
        }
    }
}
