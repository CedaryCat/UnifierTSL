using Atelier.Presentation.Window.Drafting;
using Atelier.Session;
using Microsoft.CodeAnalysis.CSharp;
using UnifierTSL.Contracts.Sessions;
using UnifierTSL.Contracts.Terminal;

namespace Atelier.Presentation.Window.Formatting {
    internal static class ReplDraftFormatter {
        public static DraftFormatResult? TryRewrite(
            ClientBufferedEditorState currentState,
            DraftSnapshot currentDraft,
            ReplFormatTrigger? formatTrigger,
            CSharpParseOptions parseOptions) {
            if (currentState.Kind != EditorPaneKind.MultiLine || formatTrigger is not { } trigger) {
                return null;
            }

            if (trigger.InsertedText is not ("{" or ";")) {
                return null;
            }

            var rewrite = ApplyTrigger(DraftRewrite.Start(currentDraft), trigger, parseOptions);
            rewrite = ReplSpacingNormalizer.NormalizeGenericAngleSpacing(rewrite, parseOptions);
            var (state, draft) = DraftProjection.CreateRewrittenState(currentState, rewrite.Draft);
            return currentState.ContentEquals(state)
                ? null
                : new DraftFormatResult(state, draft, rewrite.EditBatches);
        }

        public static DraftFormatResult? TryRewriteAll(
            ClientBufferedEditorState currentState,
            DraftSnapshot currentDraft,
            string indentUnit,
            CSharpParseOptions parseOptions) {
            if (currentState.Kind != EditorPaneKind.MultiLine) {
                return null;
            }

            try {
                var root = CSharpSyntaxTree.ParseText(currentDraft.SourceText, parseOptions).GetRoot();
                var formatEdits = RoslynFormattingWorkspace.GetFormattedTextChanges(root, root.FullSpan);
                var rewrite = formatEdits.IsDefaultOrEmpty
                    ? DraftRewrite.Start(currentDraft)
                    : TextEditPlan
                        .FromEdits(currentDraft.SourceText, currentDraft.SourceCaretIndex, formatEdits)
                        .ApplyTo(DraftRewrite.Start(currentDraft));
                rewrite = ReplIndentNormalizer.Normalize(rewrite, indentUnit, parseOptions);
                var (state, draft) = DraftProjection.CreateRewrittenState(currentState, rewrite.Draft);
                return currentState.ContentEquals(state)
                    ? null
                    : new DraftFormatResult(state, draft, rewrite.EditBatches);
            }
            catch {
                return null;
            }
        }

        private static DraftRewrite ApplyTrigger(
            DraftRewrite rewrite,
            ReplFormatTrigger trigger,
            CSharpParseOptions parseOptions) {
            var triggered = trigger.InsertedText == "{"
                ? OpeningBraceFormatter.TryRewrite(rewrite, trigger.InsertedStartIndex, parseOptions)
                : TryRewriteSemicolon(rewrite, trigger.InsertedStartIndex, parseOptions);
            return triggered ?? rewrite;
        }

        private static DraftRewrite? TryRewriteSemicolon(
            DraftRewrite rewrite,
            int semicolonIndex,
            CSharpParseOptions parseOptions) {
            if (!SemicolonCompleter.TryComplete(rewrite, semicolonIndex, parseOptions, out var completion)) {
                return TypedCharacterFormatter.TryFormat(rewrite, ';', semicolonIndex, parseOptions);
            }

            if (!completion.ShouldFormat) {
                return completion.Rewrite;
            }

            var formatted = TypedCharacterFormatter.TryFormat(
                DraftRewrite.Start(completion.Rewrite.Draft),
                ';',
                completion.FormatIndex,
                parseOptions);
            return formatted is null
                ? completion.Rewrite
                : DraftRewrite.Start(
                    formatted.Value.Draft,
                    [.. completion.Rewrite.EditBatches, .. formatted.Value.EditBatches]);
        }
    }
}
