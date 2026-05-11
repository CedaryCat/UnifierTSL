using Atelier.Presentation.Window.Formatting;
using Atelier.Session;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using UnifierTSL.Contracts.Sessions;
using UnifierTSL.TextEditing;

namespace Atelier.Presentation.Window.Drafting {
    internal sealed record DraftContext(
        ClientBufferedEditorState VisibleState,
        DraftSnapshot Draft);

    internal readonly record struct PairRewriteResult(
        ClientBufferedEditorState State,
        DraftSnapshot BaseDraft,
        DraftSnapshot Draft,
        ReplFormatTrigger? FormatTrigger,
        bool WasRewritten,
        VisibleEdit? VisibleEdit,
        SourceEdit? SourceEdit,
        ImmutableArray<SourceEditBatch> BaseEditBatches,
        ImmutableArray<SourceEditBatch> SourceEditBatches);

    internal static class ReplPairEngine {
        private sealed record ExpansionPair(long PairId, VirtualPairKind Kind, int OpenerIndex, int CloserIndex);

        public static PairRewriteResult Rewrite(
            DraftContext previousContext,
            ClientBufferedEditorState currentState,
            string indentUnit,
            CSharpParseOptions parseOptions,
            bool useKAndRBraceStyle) {

            if (!VisibleEditSolver.TrySolve(previousContext.VisibleState, currentState, out var visibleEdit)) {
                return Recover(currentState);
            }

            if (visibleEdit.Kind is VisibleEditKind.NoOp or VisibleEditKind.CaretMove) {
                var draft = VisibleSourceMapper.TryMapEncodedPosition(
                    previousContext.Draft,
                    visibleEdit.CurrentCaretIndex,
                    preferEnd: false,
                    out var sourceCaret)
                ? previousContext.Draft.With(sourceCaretIndex: sourceCaret)
                : previousContext.Draft;
                return RewriteFromDraft(
                    currentState,
                    draft,
                    draft,
                    visibleEdit,
                    null,
                    [],
                    indentUnit,
                    parseOptions,
                    useKAndRBraceStyle);
            }

            if (previousContext.VisibleState.ClientBufferRevision + 1 != currentState.ClientBufferRevision) {
                return Recover(currentState);
            }

            if (!VisibleSourceMapper.TryMapVisibleEdit(previousContext.Draft, visibleEdit, out var sourceEdit)) {
                return Recover(currentState);
            }

            var initialRewrite = DraftRewrite
                .Start(previousContext.Draft)
                .Apply(ExpandEmptyPairDeletion(previousContext.Draft, sourceEdit));
            var currentDraft = AlignDraftToVisibleState(initialRewrite.Draft, currentState);
            return RewriteFromDraft(
                currentState,
                currentDraft,
                currentDraft,
                visibleEdit,
                sourceEdit,
                initialRewrite.EditBatches,
                indentUnit,
                parseOptions,
                useKAndRBraceStyle);
        }

        public static bool TryRewritePendingProjection(
            DraftSnapshot baseDraft,
            DraftSnapshot pendingDraft,
            ClientBufferedEditorState currentState,
            VisibleEdit visibleEdit,
            SourceEdit sourceEdit,
            string indentUnit,
            CSharpParseOptions parseOptions,
            bool useKAndRBraceStyle,
            out PairRewriteResult result) {
            result = default;
            if (sourceEdit.Kind != SourceEditKind.Text
                || !sourceEdit.IsInsertion
                || sourceEdit.StartIndex != baseDraft.SourceCaretIndex) {
                return false;
            }

            var pendingEdit = sourceEdit with { StartIndex = pendingDraft.SourceCaretIndex };
            var rewrite = DraftRewrite.Start(pendingDraft);
            if (TryMaterializePendingVirtualCloser(rewrite, pendingEdit, out var materializedRewrite)) {
                result = RewriteFromDraft(
                    currentState,
                    materializedRewrite.Draft,
                    materializedRewrite.Draft,
                    visibleEdit,
                    pendingEdit,
                    materializedRewrite.EditBatches,
                    indentUnit,
                    parseOptions,
                    useKAndRBraceStyle);
                return true;
            }

            rewrite = rewrite.Apply(ExpandEmptyPairDeletion(pendingDraft, pendingEdit));
            result = RewriteFromDraft(
                currentState,
                rewrite.Draft,
                rewrite.Draft,
                visibleEdit,
                pendingEdit,
                rewrite.EditBatches,
                indentUnit,
                parseOptions,
                useKAndRBraceStyle);
            return true;
        }

        public static DraftSnapshot DecodeBaseDraft(ClientBufferedEditorState visibleState) {
            return DraftMarkers.RecoverWithoutVirtualPairs(
                visibleState.BufferText,
                visibleState.Markers,
                visibleState.CaretIndex);
        }

        private static PairRewriteResult RewriteFromDraft(
            ClientBufferedEditorState currentState,
            DraftSnapshot baseDraft,
            DraftSnapshot currentDraft,
            VisibleEdit visibleEdit,
            SourceEdit? sourceEdit,
            ImmutableArray<SourceEditBatch> baseEditBatches,
            string indentUnit,
            CSharpParseOptions parseOptions,
            bool useKAndRBraceStyle) {
            var rewrite = DraftRewrite.Start(currentDraft, baseEditBatches);
            ReplFormatTrigger? formatTrigger = sourceEdit is { RemovedLength: 0, InsertedLength: > 0 } triggerEdit
                ? new ReplFormatTrigger(triggerEdit.InsertedText, triggerEdit.StartIndex)
                : null;

            if (TryRewriteExitedPairs(rewrite, visibleEdit, parseOptions, out var exitedRewrite)) {
                rewrite = exitedRewrite;
            }
            else if (sourceEdit is { } pairEdit
                && TryRewritePairInsertion(rewrite, pairEdit, parseOptions, out var insertionRewrite)) {
                rewrite = insertionRewrite;
            }
            else if (formatTrigger is { IsNewLineInsertion: true }
                && (TryRewritePairExpansion(
                        rewrite,
                        formatTrigger.Value,
                        indentUnit,
                        parseOptions,
                        useKAndRBraceStyle,
                        out var expansionRewrite)
                    || TryRewriteContinuationIndent(
                        rewrite,
                        formatTrigger.Value,
                        indentUnit,
                        out expansionRewrite))) {
                rewrite = expansionRewrite;
            }

            var rewritten = DraftProjection.CreateRewrittenState(currentState, rewrite.Draft);
            return new PairRewriteResult(
                rewritten.State,
                baseDraft,
                rewritten.Draft,
                formatTrigger,
                !rewritten.State.ContentEquals(currentState),
                visibleEdit,
                sourceEdit,
                baseEditBatches,
                rewrite.EditBatches);
        }

        private static PairRewriteResult Recover(ClientBufferedEditorState currentState) {
            var draft = DraftMarkers.RecoverWithoutVirtualPairs(
                currentState.BufferText,
                currentState.Markers,
                currentState.CaretIndex);
            var rewritten = DraftProjection.CreateRewrittenState(currentState, draft);
            return new PairRewriteResult(
                rewritten.State,
                rewritten.Draft,
                rewritten.Draft,
                null,
                !rewritten.State.ContentEquals(currentState),
                null,
                null,
                [],
                []);
        }

        private static SourceEdit ExpandEmptyPairDeletion(DraftSnapshot previousDraft, SourceEdit edit) {
            if (edit.Kind != SourceEditKind.Text || !edit.IsDeletion) {
                return edit;
            }

            var text = previousDraft.SourceText;
            var start = Math.Clamp(edit.StartIndex, 0, text.Length);
            var end = Math.Clamp(edit.EndIndex, start, text.Length);
            foreach (var entry in previousDraft.PairLedger.Entries
                .Where(entry => edit.StartIndex <= entry.OpenerIndex && edit.EndIndex > entry.OpenerIndex)) {
                if (!IsWhitespaceOnly(text, entry.OpenerIndex + 1, entry.CloserIndex)) {
                    continue;
                }

                start = Math.Min(start, Math.Clamp(entry.OpenerIndex, 0, text.Length));
                end = Math.Max(end, Math.Clamp(entry.CloserEndIndex, start, text.Length));
            }

            if (start == edit.StartIndex && end == edit.EndIndex) {
                return edit;
            }

            if (end < text.Length && (text[end] is ' ' or '\t') && (start == 0 || text[start - 1] is ' ' or '\t')) {
                end++;
            }
            else if (start > 0 && end == text.Length && (text[start - 1] is ' ' or '\t')) {
                start--;
            }

            return new SourceEdit(start, end - start, string.Empty);
        }

        private static DraftSnapshot AlignDraftToVisibleState(DraftSnapshot draft, ClientBufferedEditorState visibleState) {
            var text = visibleState.BufferText ?? string.Empty;
            var visibleMarkers = EditorTextMarkerOps.Normalize(visibleState.Markers, text.Length)
                .OrderBy(static marker => marker.StartIndex)
                .ThenBy(static marker => marker.Length)
                .ToArray();
            var sourceMarkers = draft.SourceMarkers
                .OrderBy(static marker => marker.SourceStartIndex)
                .ThenBy(static marker => marker.PairId)
                .ToArray();
            if (visibleMarkers.Length != sourceMarkers.Length) {
                return draft;
            }

            List<SourceTextMarker> aligned = [];
            for (var index = 0; index < sourceMarkers.Length; index++) {
                var sourceMarker = sourceMarkers[index];
                var visibleMarker = visibleMarkers[index];
                if (!string.Equals(
                    DraftMarkers.GetBaseKey(sourceMarker.BaseKey),
                    DraftMarkers.GetBaseKey(visibleMarker.Key),
                    StringComparison.Ordinal)) {
                    return draft;
                }

                aligned.Add(sourceMarker with {
                    EncodedStartIndex = visibleMarker.StartIndex,
                    EncodedLength = visibleMarker.Length,
                });
            }

            var ledger = draft.PairLedger.ApplyEncodedProjection(aligned);
            return new DraftSnapshot(draft.SourceText, draft.SourceCaretIndex, aligned, ledger);
        }

        private static bool TryRewriteExitedPairs(
            DraftRewrite rewrite,
            VisibleEdit visibleEdit,
            CSharpParseOptions parseOptions,
            out DraftRewrite rewrittenRewrite) {
            rewrittenRewrite = rewrite;
            var draft = rewrite.Draft;
            if (visibleEdit.Kind != VisibleEditKind.CaretMove) {
                return false;
            }

            var previousCaret = VisibleSourceMapper.TryMapEncodedPosition(
                draft,
                visibleEdit.PreviousCaretIndex,
                preferEnd: false,
                out var sourcePreviousCaret)
                ? sourcePreviousCaret
                : draft.SourceCaretIndex;
            var exitedMarkers = VirtualPairAnalyzer.ResolveExitedMarkers(
                draft,
                previousCaret,
                draft.SourceCaretIndex,
                parseOptions);
            if (exitedMarkers.IsDefaultOrEmpty) {
                return false;
            }

            rewrittenRewrite = rewrite.RemovePairs(exitedMarkers.Select(static marker => marker.PairId));
            return true;
        }

        private static bool TryRewritePairInsertion(
            DraftRewrite rewrite,
            SourceEdit sourceEdit,
            CSharpParseOptions parseOptions,
            out DraftRewrite rewrittenRewrite) {
            rewrittenRewrite = rewrite;
            if (sourceEdit.RemovedLength != 0 || sourceEdit.InsertedLength != 1) {
                return false;
            }

            return sourceEdit.InsertedText switch {
                "(" => TryInsertDelimitedPair(
                    rewrite,
                    sourceEdit.StartIndex,
                    parseOptions,
                    SyntaxKind.OpenParenToken,
                    ')',
                    DraftMarkers.CloseParenKey,
                    VirtualPairKind.Parenthesis,
                    out rewrittenRewrite),
                "[" => TryInsertDelimitedPair(
                    rewrite,
                    sourceEdit.StartIndex,
                    parseOptions,
                    SyntaxKind.OpenBracketToken,
                    ']',
                    DraftMarkers.CloseBracketKey,
                    VirtualPairKind.Bracket,
                    out rewrittenRewrite),
                "<" => TryInsertAnglePair(rewrite, sourceEdit.StartIndex, parseOptions, out rewrittenRewrite),
                "\"" => TryInsertQuotePair(
                    rewrite,
                    sourceEdit.StartIndex,
                    parseOptions,
                    isDoubleQuote: true,
                    out rewrittenRewrite),
                "'" => TryInsertQuotePair(
                    rewrite,
                    sourceEdit.StartIndex,
                    parseOptions,
                    isDoubleQuote: false,
                    out rewrittenRewrite),
                "{" => TryInsertBracePair(rewrite, sourceEdit.StartIndex, parseOptions, out rewrittenRewrite),
                _ => false,
            };
        }

        private static bool TryMaterializePendingVirtualCloser(
            DraftRewrite rewrite,
            SourceEdit pendingEdit,
            out DraftRewrite materializedRewrite) {
            materializedRewrite = rewrite;
            var pendingDraft = rewrite.Draft;
            if (!pendingEdit.IsInsertion) {
                return false;
            }

            var marker = pendingDraft.SourceMarkers.FirstOrDefault(marker =>
                marker.PairId > 0
                && marker.SourceStartIndex == pendingEdit.StartIndex
                && marker.SourceLength == pendingEdit.InsertedLength
                && DraftMarkers.TryGetSourceText(marker.BaseKey, out var sourceText)
                && string.Equals(sourceText, pendingEdit.InsertedText, StringComparison.Ordinal));
            if (marker is not null) {
                var ledger = pendingDraft.PairLedger.RemovePair(marker.PairId);
                materializedRewrite = rewrite.WithDraft(pendingDraft.With(
                    sourceCaretIndex: marker.SourceStartIndex + marker.SourceLength,
                    pairLedger: ledger));

                return true;
            }

            return TryMaterializeSeparatedPendingVirtualCloser(rewrite, pendingEdit, out materializedRewrite);
        }

        private static bool TryMaterializeSeparatedPendingVirtualCloser(
            DraftRewrite rewrite,
            SourceEdit pendingEdit,
            out DraftRewrite materializedRewrite) {
            materializedRewrite = rewrite;
            var pendingDraft = rewrite.Draft;
            var editEnd = pendingEdit.StartIndex + pendingEdit.InsertedLength;
            var marker = pendingDraft.SourceMarkers.FirstOrDefault(marker =>
                marker.PairId > 0
                && marker.SourceStartIndex >= editEnd
                && IsMaterializableSeparatedPair(pendingDraft.PairLedger, marker.PairId)
                && DraftMarkers.TryGetSourceText(marker.BaseKey, out var sourceText)
                && string.Equals(sourceText, pendingEdit.InsertedText, StringComparison.Ordinal)
                && IsWhitespaceOnly(pendingDraft.SourceText, editEnd, marker.SourceStartIndex));
            if (marker is null) {
                return false;
            }

            var materializeEdit = new SourceEdit(
                pendingEdit.StartIndex,
                marker.SourceStartIndex + marker.SourceLength - pendingEdit.StartIndex,
                pendingEdit.InsertedText);
            var text = DraftMarkers.ApplySourceEditText(pendingDraft.SourceText, materializeEdit);
            materializedRewrite = rewrite.WithState(
                text,
                pendingEdit.StartIndex + pendingEdit.InsertedLength,
                pendingDraft.PairLedger.ApplySourceEdit(materializeEdit),
                new[] { materializeEdit });

            return true;
        }

        private static bool IsMaterializableSeparatedPair(VirtualPairLedger ledger, long pairId) {
            var entry = ledger.Entries.FirstOrDefault(entry => entry.PairId == pairId);
            return entry?.Kind is VirtualPairKind.Parenthesis or VirtualPairKind.Bracket or VirtualPairKind.Brace or VirtualPairKind.Angle;
        }

        private static bool TryRewritePairExpansion(
            DraftRewrite rewrite,
            ReplFormatTrigger formatTrigger,
            string indentUnit,
            CSharpParseOptions parseOptions,
            bool useKAndRBraceStyle,
            out DraftRewrite rewrittenRewrite) {
            rewrittenRewrite = rewrite;
            var expansionRewrite = rewrite;
            var expansionDraft = expansionRewrite.Draft;
            var expansionTrigger = formatTrigger;
            var analysis = VirtualPairAnalyzer.Analyze(expansionDraft, parseOptions);
            if (ResolveExpansionPair(expansionDraft, analysis) is not { } activePair
                || activePair.Kind is not (VirtualPairKind.Parenthesis or VirtualPairKind.Bracket or VirtualPairKind.Brace)
                || !IsPairExpansionGap(expansionDraft.SourceText, activePair, formatTrigger)) {
                return false;
            }

            if (activePair.Kind == VirtualPairKind.Brace) {
                var normalized = BracePairLayoutNormalizer.NormalizeOpeningBraceLayout(
                    expansionDraft.SourceText,
                    activePair.OpenerIndex,
                    parseOptions,
                    useKAndRBraceStyle);
                if (!string.Equals(normalized.Text, expansionDraft.SourceText, StringComparison.Ordinal)) {
                    expansionTrigger = formatTrigger with {
                        InsertedStartIndex = formatTrigger.InsertedStartIndex + normalized.BraceIndex - activePair.OpenerIndex,
                    };
                    var layoutCaret = Math.Clamp(
                        expansionDraft.SourceCaretIndex + normalized.BraceIndex - activePair.OpenerIndex,
                        0,
                        normalized.Text.Length);
                    expansionRewrite = expansionRewrite.ApplyBatch(normalized.Edits, layoutCaret);
                    expansionDraft = expansionRewrite.Draft;

                    analysis = VirtualPairAnalyzer.Analyze(expansionDraft, parseOptions);
                    if (ResolveExpansionPair(expansionDraft, analysis) is not { } normalizedPair
                        || normalizedPair.Kind != VirtualPairKind.Brace
                        || !IsPairExpansionGap(expansionDraft.SourceText, normalizedPair, expansionTrigger)) {
                        return false;
                    }

                    activePair = normalizedPair;
                }
            }

            return TryRewriteNewLineLayout(
                expansionRewrite,
                activePair.OpenerIndex,
                expansionTrigger.InsertedStartIndex,
                indentUnit,
                activePair.CloserIndex,
                includeBodyLine: true,
                out rewrittenRewrite);
        }

        private static ExpansionPair? ResolveExpansionPair(
            DraftSnapshot draft,
            VirtualPairAnalysis analysis) {
            if (analysis.ActivePair is { } activePair) {
                return new ExpansionPair(activePair.PairId, activePair.Kind, activePair.OpenerIndex, activePair.CloserIndex);
            }

            foreach (var entry in draft.PairLedger.Entries
                .Where(entry => draft.SourceCaretIndex == entry.CloserIndex)
                .OrderByDescending(static entry => entry.OpenerIndex)) {
                return new ExpansionPair(entry.PairId, entry.Kind, entry.OpenerIndex, entry.CloserIndex);
            }

            return null;
        }

        private static bool TryRewriteContinuationIndent(
            DraftRewrite rewrite,
            ReplFormatTrigger formatTrigger,
            string indentUnit,
            out DraftRewrite rewrittenRewrite) {
            rewrittenRewrite = rewrite;
            var draft = rewrite.Draft;
            var text = draft.SourceText;
            var newLineIndex = formatTrigger.InsertedStartIndex;
            if (newLineIndex < 0
                || newLineIndex >= text.Length
                || text[newLineIndex] != '\n'
                || !TryResolveContinuationOpener(text, newLineIndex, out var openerIndex, out var closer)) {
                return false;
            }

            var newLineStart = newLineIndex + 1;
            var lineEnd = LogicalLineBounds.GetContentEndIndex(text, newLineStart);
            var closerIndex = TryResolveCloserLineIndex(text, draft.SourceCaretIndex, lineEnd, closer, out var resolvedCloserIndex)
                ? resolvedCloserIndex
                : (int?)null;
            return TryRewriteNewLineLayout(
                rewrite,
                openerIndex,
                newLineIndex,
                indentUnit,
                closerIndex,
                includeBodyLine: false,
                out rewrittenRewrite);
        }

        private static bool TryRewriteNewLineLayout(
            DraftRewrite rewrite,
            int openerIndex,
            int newLineIndex,
            string indentUnit,
            int? closerIndex,
            bool includeBodyLine,
            out DraftRewrite rewrittenRewrite) {
            rewrittenRewrite = rewrite;
            var text = rewrite.Draft.SourceText;
            if (openerIndex < 0
                || openerIndex >= text.Length
                || newLineIndex <= openerIndex
                || newLineIndex >= text.Length
                || text[newLineIndex] != '\n') {
                return false;
            }

            var outerIndent = GetLineIndent(text, openerIndex);
            var bodyIndent = outerIndent + indentUnit;
            var editStart = openerIndex + 1;
            var editEnd = closerIndex is { } closer
                ? Math.Clamp(closer, editStart, text.Length)
                : GetHorizontalWhitespaceEnd(
                    text,
                    newLineIndex + 1,
                    LogicalLineBounds.GetContentEndIndex(text, newLineIndex + 1));
            var replacement = includeBodyLine
                ? "\n" + bodyIndent + "\n" + outerIndent
                : "\n" + (closerIndex.HasValue ? outerIndent : bodyIndent);
            if (editEnd - editStart == replacement.Length
                && string.CompareOrdinal(text, editStart, replacement, 0, replacement.Length) == 0) {
                rewrittenRewrite = rewrite;
                return true;
            }

            var edit = new SourceEdit(editStart, editEnd - editStart, replacement);
            var caretIndent = includeBodyLine || !closerIndex.HasValue ? bodyIndent : outerIndent;
            rewrittenRewrite = rewrite.Apply(edit, editStart + 1 + caretIndent.Length);
            return true;
        }

        private static bool TryResolveContinuationOpener(
            string text,
            int newLineIndex,
            out int openerIndex,
            out char closer) {
            openerIndex = -1;
            closer = '\0';
            var lineStart = LogicalLineBounds.GetStartIndex(text, newLineIndex);
            var index = newLineIndex - 1;
            while (index >= lineStart && text[index] is ' ' or '\t') {
                index--;
            }

            if (index < lineStart) {
                return false;
            }

            closer = text[index] switch {
                '(' => ')',
                '[' => ']',
                '{' => '}',
                _ => '\0',
            };
            if (closer == '\0') {
                return false;
            }

            openerIndex = index;
            return true;
        }

        private static bool TryResolveCloserLineIndex(
            string text,
            int startIndex,
            int lineEnd,
            char closer,
            out int closerIndex) {
            var end = Math.Clamp(lineEnd, 0, text.Length);
            var index = GetHorizontalWhitespaceEnd(text, startIndex, end);
            if (index < end && text[index] == closer) {
                closerIndex = index;
                return true;
            }

            closerIndex = -1;
            return false;
        }

        private static bool TryInsertDelimitedPair(
            DraftRewrite rewrite,
            int openerIndex,
            CSharpParseOptions parseOptions,
            SyntaxKind openerKind,
            char closer,
            string markerKey,
            VirtualPairKind pairKind,
            out DraftRewrite rewrittenRewrite) {
            rewrittenRewrite = rewrite;
            var draft = rewrite.Draft;
            if (!MatchesTypedTokenKind(draft.SourceText, openerIndex, parseOptions, openerKind)
                || HasConcreteSourceCloserAt(draft.SourceText, draft.SourceMarkers, openerIndex + 1, closer)) {
                return false;
            }

            return InsertVirtualCloser(
                rewrite,
                pairKind,
                markerKey,
                openerIndex,
                openerIndex + 1,
                closer.ToString(),
                1,
                openerIndex + 1,
                out rewrittenRewrite);
        }

        private static bool TryInsertAnglePair(
            DraftRewrite rewrite,
            int openerIndex,
            CSharpParseOptions parseOptions,
            out DraftRewrite rewrittenRewrite) {
            rewrittenRewrite = rewrite;
            var draft = rewrite.Draft;
            if (!MatchesTypedGenericAngleOpener(draft.SourceText, openerIndex, parseOptions)
                || HasConcreteSourceCloserAt(draft.SourceText, draft.SourceMarkers, openerIndex + 1, '>')) {
                return false;
            }

            return InsertVirtualCloser(
                rewrite,
                VirtualPairKind.Angle,
                DraftMarkers.CloseAngleKey,
                openerIndex,
                openerIndex + 1,
                ">",
                1,
                openerIndex + 1,
                out rewrittenRewrite);
        }

        private static bool TryInsertQuotePair(
            DraftRewrite rewrite,
            int openerIndex,
            CSharpParseOptions parseOptions,
            bool isDoubleQuote,
            out DraftRewrite rewrittenRewrite) {
            rewrittenRewrite = rewrite;
            var draft = rewrite.Draft;
            var closer = isDoubleQuote ? '"' : '\'';
            if (!MatchesTypedQuote(draft.SourceText, openerIndex, parseOptions, isDoubleQuote)
                || HasConcreteSourceCloserAt(draft.SourceText, draft.SourceMarkers, openerIndex + 1, closer)) {
                return false;
            }

            return InsertVirtualCloser(
                rewrite,
                isDoubleQuote ? VirtualPairKind.DoubleQuote : VirtualPairKind.SingleQuote,
                isDoubleQuote ? DraftMarkers.DoubleQuoteKey : DraftMarkers.SingleQuoteKey,
                openerIndex,
                openerIndex + 1,
                closer.ToString(),
                1,
                openerIndex + 1,
                out rewrittenRewrite);
        }

        private static bool TryInsertBracePair(
            DraftRewrite rewrite,
            int openerIndex,
            CSharpParseOptions parseOptions,
            out DraftRewrite rewrittenRewrite) {
            rewrittenRewrite = rewrite;
            var draft = rewrite.Draft;
            if (!MatchesTypedTokenKind(draft.SourceText, openerIndex, parseOptions, SyntaxKind.OpenBraceToken)) {
                return false;
            }

            return InsertVirtualCloser(
                rewrite,
                VirtualPairKind.Brace,
                DraftMarkers.CloseBraceKey,
                openerIndex,
                openerIndex + 1,
                " }",
                1,
                openerIndex + 2,
                out rewrittenRewrite);
        }

        private static bool InsertVirtualCloser(
            DraftRewrite rewrite,
            VirtualPairKind kind,
            string markerKey,
            int openerIndex,
            int insertIndex,
            string insertedText,
            int closerLength,
            int closerIndex,
            out DraftRewrite rewrittenRewrite) {
            var edit = new SourceEdit(insertIndex, 0, insertedText);
            var draft = rewrite.Draft;
            var text = DraftMarkers.ApplySourceEditText(draft.SourceText, edit);
            var ledger = draft.PairLedger
                .ApplySourceEdit(edit)
                .CreatePair(kind, markerKey, openerIndex, closerIndex, closerLength, out _);
            rewrittenRewrite = rewrite.WithState(
                text,
                closerIndex,
                ledger,
                new[] { edit });
            return true;
        }

        private static bool MatchesTypedTokenKind(
            string text,
            int tokenIndex,
            CSharpParseOptions parseOptions,
            SyntaxKind kind) {
            var root = CSharpSyntaxTree.ParseText(text, parseOptions).GetRoot();
            var token = root.FindToken(tokenIndex, findInsideTrivia: true);
            return token.RawKind != 0
                && token.SpanStart == tokenIndex
                && token.Span.Length == 1
                && token.IsKind(kind);
        }

        private static bool MatchesTypedGenericAngleOpener(
            string text,
            int tokenIndex,
            CSharpParseOptions parseOptions) {
            if (!MatchesTypedTokenKind(text, tokenIndex, parseOptions, SyntaxKind.LessThanToken)) {
                return false;
            }

            var root = CSharpSyntaxTree.ParseText(text, parseOptions).GetRoot();
            var token = root.FindToken(tokenIndex, findInsideTrivia: true);
            return IsTypeArgumentListOpener(token)
                || IsOpenGenericSignatureHelpOpener(root, tokenIndex);
        }

        private static bool IsTypeArgumentListOpener(SyntaxToken token) {
            return token.Parent is TypeArgumentListSyntax typeArgumentList
                && typeArgumentList.LessThanToken == token
                && typeArgumentList.Parent is GenericNameSyntax;
        }

        private static bool IsOpenGenericSignatureHelpOpener(SyntaxNode root, int tokenIndex) {
            if (root.FindToken(tokenIndex).Parent is not BinaryExpressionSyntax {
                    RawKind: (int)SyntaxKind.LessThanExpression
                } binaryExpression
                || binaryExpression.OperatorToken.SpanStart != tokenIndex) {
                return false;
            }

            return binaryExpression.Left.DescendantNodesAndSelf().LastOrDefault() is IdentifierNameSyntax identifier
                && identifier.Identifier.ValueText is { Length: > 0 } name
                && char.IsUpper(name[0]);
        }

        private static bool MatchesTypedQuote(string text, int quoteIndex, CSharpParseOptions parseOptions, bool isDoubleQuote) {
            if (quoteIndex < 0
                || quoteIndex >= text.Length
                || text[quoteIndex] != (isDoubleQuote ? '"' : '\'')) {
                return false;
            }

            var root = CSharpSyntaxTree.ParseText(text, parseOptions).GetRoot();
            var token = root.FindToken(quoteIndex, findInsideTrivia: true);
            if (token.RawKind == 0 || quoteIndex < token.SpanStart || quoteIndex >= token.Span.End) {
                return false;
            }

            if (!isDoubleQuote) {
                return token.IsKind(SyntaxKind.CharacterLiteralToken) && token.SpanStart == quoteIndex;
            }

            if (!token.IsKind(SyntaxKind.StringLiteralToken)) {
                return false;
            }

            return quoteIndex == token.SpanStart
                || quoteIndex == token.SpanStart + 1 && IsStringPrefix(text, token.SpanStart, quoteIndex)
                || quoteIndex == token.SpanStart + 2 && IsStringPrefix(text, token.SpanStart, quoteIndex);
        }

        private static bool IsStringPrefix(string text, int tokenStart, int quoteIndex) {
            if (quoteIndex <= tokenStart || quoteIndex >= text.Length || text[quoteIndex] != '"') {
                return false;
            }

            for (var index = tokenStart; index < quoteIndex; index++) {
                if (text[index] is not ('@' or '$')) {
                    return false;
                }
            }

            return true;
        }

        private static bool HasConcreteSourceCloserAt(
            string text,
            IReadOnlyList<SourceTextMarker> markers,
            int sourceIndex,
            char closer) {
            return sourceIndex >= 0
                && sourceIndex < text.Length
                && text[sourceIndex] == closer
                && !markers.Any(marker => sourceIndex >= marker.SourceStartIndex
                    && sourceIndex < marker.SourceStartIndex + marker.SourceLength);
        }

        private static string GetLineIndent(string text, int index) {
            var lineStart = LogicalLineBounds.GetStartIndex(text, index);
            var lineEnd = LogicalLineBounds.GetContentEndIndex(text, lineStart);
            return text[lineStart..GetHorizontalWhitespaceEnd(text, lineStart, lineEnd)];
        }

        private static bool IsPairExpansionGap(string text, ExpansionPair pair, ReplFormatTrigger formatTrigger) {
            var insertedStart = formatTrigger.InsertedStartIndex;
            var insertedEnd = insertedStart + formatTrigger.InsertedText.Length;
            return insertedStart >= pair.OpenerIndex + 1
                && insertedEnd <= pair.CloserIndex
                && IsHorizontalWhitespaceOnly(text, pair.OpenerIndex + 1, insertedStart)
                && IsHorizontalWhitespaceOnly(text, insertedEnd, pair.CloserIndex);
        }

        private static bool IsHorizontalWhitespaceOnly(string text, int startIndex, int endIndex) {
            var start = Math.Clamp(startIndex, 0, text.Length);
            var end = Math.Clamp(endIndex, start, text.Length);
            return GetHorizontalWhitespaceEnd(text, start, end) == end;
        }

        private static int GetHorizontalWhitespaceEnd(string text, int startIndex, int endIndex) {
            var index = Math.Clamp(startIndex, 0, text.Length);
            var end = Math.Clamp(endIndex, index, text.Length);
            while (index < end && text[index] is ' ' or '\t') {
                index++;
            }

            return index;
        }

        private static bool IsWhitespaceOnly(string text, int startIndex, int endIndex) {
            for (var index = Math.Max(0, startIndex); index < Math.Min(endIndex, text.Length); index++) {
                if (!char.IsWhiteSpace(text[index])) {
                    return false;
                }
            }

            return true;
        }

    }
}
