using Atelier.Presentation.Prompt;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.Tags;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using UnifierTSL.Surface.Prompting.Model;
using UnifierTSL.Contracts.Display;
using RoslynCompletionItem = Microsoft.CodeAnalysis.Completion.CompletionItem;

namespace Atelier.Session.Roslyn
{
    internal static class CompletionProvider
    {
        public static async Task<CompletionInfo> GetCompletionInfoAsync(
            Document document,
            SourceText sourceText,
            SyntheticDocument syntheticDocument,
            CancellationToken cancellationToken) {

            try {
                var service = CompletionService.GetService(document);
                if (service is null) {
                    return CompletionInfo.Empty;
                }

                var completionList = await service.GetCompletionsAsync(document, syntheticDocument.SyntheticCaretIndex, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (completionList is null) {
                    return CompletionInfo.Empty;
                }

                var activationMode = ResolveCompletionTriggerMode(service, sourceText, completionList.Span, syntheticDocument);
                var items = ImmutableArray.CreateBuilder<PromptCompletionItem>();
                var weight = 1000;
                var summaryBudget = 12;
                var filterText = GetCompletionFilterText(sourceText, completionList.Span, syntheticDocument.SyntheticCaretIndex);
                var rawItems = ImmutableArray.CreateRange(completionList.ItemsList);
                var filteredItems = string.IsNullOrEmpty(filterText)
                    ? rawItems
                    : service.FilterItems(document, rawItems, filterText);
                foreach (var item in filteredItems) {
                    var edit = await TryCreateCompletionEditAsync(service, document, item, syntheticDocument, cancellationToken).ConfigureAwait(false);
                    if (edit is null) {
                        continue;
                    }

                    bool includeRichDetails = summaryBudget > 0;
                    var summary = includeRichDetails
                        ? await TryBuildCompletionSummaryAsync(service, document, item, cancellationToken).ConfigureAwait(false)
                        : CompletionSummary.Empty;
                    var displayText = $"{item.DisplayTextPrefix}{item.DisplayText}{item.DisplayTextSuffix}";
                    var secondaryLabel = item.InlineDescription ?? string.Empty;
                    var trailingLabel = BuildCompletionTrailingLabel(item);
                    var displayStyleId = RoslynStyleResolver.ResolveCompletionDisplayStyleId(item.Tags, item.DisplayText);

                    items.Add(new PromptCompletionItem {
                        Id = CreateCompletionId(item, edit.Value, secondaryLabel, trailingLabel),
                        DisplayText = displayText,
                        SecondaryDisplayText = secondaryLabel,
                        TrailingDisplayText = trailingLabel,
                        SummaryText = summary.Text,
                        SummaryHighlights = summary.Highlights,
                        DisplayStyleId = displayStyleId,
                        SecondaryDisplayStyleId = SurfaceStyleCatalog.CompletionPopupText,
                        TrailingDisplayStyleId = SurfaceStyleCatalog.CompletionPopupText,
                        SummaryStyleId = PromptStyles.CompletionSummaryText,
                        PreviewStyleId = PromptStyles.ResolveGhost(displayStyleId),
                        PrimaryEdit = edit.Value,
                        Weight = weight--,
                    });
                    if (includeRichDetails) {
                        summaryBudget--;
                    }

                    if (items.Count >= 80) {
                        break;
                    }
                }

                if (items.Count == 0) {
                    return CompletionInfo.Empty with {
                        ActivationMode = activationMode,
                    };
                }

                var results = items.ToImmutable();
                return new CompletionInfo(
                    results,
                    results[0].PrimaryEdit.Apply(syntheticDocument.DraftText),
                    activationMode);
            }
            catch (OperationCanceledException) {
                throw;
            }
            catch {
                return CompletionInfo.Empty;
            }
        }

        private static CompletionTriggerMode ResolveCompletionTriggerMode(
            CompletionService service,
            SourceText sourceText,
            TextSpan completionSpan,
            SyntheticDocument syntheticDocument) {
            var caret = syntheticDocument.SyntheticCaretIndex;
            if (caret < 0 || caret > sourceText.Length) {
                return CompletionTriggerMode.Manual;
            }

            var spanStart = Math.Clamp(completionSpan.Start, 0, sourceText.Length);
            return IsAutomaticCompletionTrigger(service, sourceText, caret)
                || IsAutomaticCompletionReplayTrigger(service, sourceText, spanStart, caret)
                || IsAutomaticCompletionReplayTrigger(service, sourceText, spanStart + 1, caret)
                ? CompletionTriggerMode.Automatic
                : CompletionTriggerMode.Manual;
        }

        private static bool IsAutomaticCompletionTrigger(CompletionService service, SourceText sourceText, int position) {
            if (position <= 0 || position > sourceText.Length) {
                return false;
            }

            var trigger = CompletionTrigger.CreateInsertionTrigger(sourceText[position - 1]);
            return service.ShouldTriggerCompletion(sourceText, position, trigger);
        }

        private static bool IsAutomaticCompletionReplayTrigger(CompletionService service, SourceText sourceText, int position, int caret) {
            if (position <= 0 || position > sourceText.Length || caret < position || caret > sourceText.Length) {
                return false;
            }

            return IsAutomaticCompletionTrigger(
                service,
                sourceText.WithChanges(new TextChange(TextSpan.FromBounds(position, caret), string.Empty)),
                position);
        }

        private static string GetCompletionFilterText(SourceText sourceText, TextSpan completionSpan, int caret) {
            var spanStart = Math.Clamp(completionSpan.Start, 0, sourceText.Length);
            var spanEnd = Math.Clamp(caret, spanStart, sourceText.Length);
            return sourceText.GetSubText(TextSpan.FromBounds(spanStart, spanEnd)).ToString();
        }

        private static bool TryMapEdit(TextChange change, SyntheticDocument syntheticDocument, out PromptTextEdit edit) {
            var span = change.Span;
            if (span.Start < syntheticDocument.DraftStart || span.End > syntheticDocument.DraftEnd) {
                edit = default;
                return false;
            }

            var draftSourceStart = span.Start - syntheticDocument.DraftStart;
            if (!syntheticDocument.TryMapDraftSourceSpan(
                    draftSourceStart,
                    span.Length,
                    out var encodedStart,
                    out var encodedLength)) {
                edit = default;
                return false;
            }

            edit = new PromptTextEdit(encodedStart, encodedLength, change.NewText ?? string.Empty);
            return true;
        }

        private static async Task<PromptTextEdit?> TryCreateCompletionEditAsync(
            CompletionService service,
            Document document,
            RoslynCompletionItem item,
            SyntheticDocument syntheticDocument,
            CancellationToken cancellationToken) {
            try {
                var change = await service.GetChangeAsync(document, item, cancellationToken: cancellationToken).ConfigureAwait(false);
                return TryMapEdit(change.TextChange, syntheticDocument, out var edit)
                    ? edit
                    : null;
            }
            catch (OperationCanceledException) {
                throw;
            }
            catch {
                return null;
            }
        }

        private static async Task<CompletionSummary> TryBuildCompletionSummaryAsync(
            CompletionService service,
            Document document,
            RoslynCompletionItem item,
            CancellationToken cancellationToken) {
            try {
                var description = await service.GetDescriptionAsync(document, item, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (description is null || description.TaggedParts.IsDefaultOrEmpty) {
                    return CompletionSummary.Empty;
                }

                StringBuilder textBuilder = new();
                List<PromptHighlightSpan> highlights = [];
                foreach (var part in description.TaggedParts) {
                    if (string.IsNullOrEmpty(part.Text)) {
                        continue;
                    }

                    var startIndex = textBuilder.Length;
                    textBuilder.Append(part.Text);
                    var styleId = RoslynStyleResolver.ResolveTaggedTextStyleId(part.Tag);
                    if (string.IsNullOrWhiteSpace(styleId)) {
                        continue;
                    }

                    highlights.Add(new PromptHighlightSpan(startIndex, part.Text.Length, PromptStyles.ResolveSummary(styleId)));
                }

                return new CompletionSummary(textBuilder.ToString(), [.. highlights]);
            }
            catch (OperationCanceledException) {
                throw;
            }
            catch {
                return CompletionSummary.Empty;
            }
        }

        private static string BuildCompletionTrailingLabel(RoslynCompletionItem item) {
            foreach (var tag in item.Tags) {
                var mapped = tag switch {
                    WellKnownTags.Class => "class",
                    WellKnownTags.Structure => "struct",
                    WellKnownTags.Interface => "interface",
                    WellKnownTags.Enum => "enum",
                    WellKnownTags.Delegate => "delegate",
                    TextTags.Record => "record",
                    TextTags.RecordStruct => "record struct",
                    WellKnownTags.Method => "method",
                    WellKnownTags.ExtensionMethod => "ext",
                    WellKnownTags.Property => "property",
                    WellKnownTags.Field => "field",
                    WellKnownTags.Event => "event",
                    WellKnownTags.Namespace => "namespace",
                    WellKnownTags.Module => "module",
                    WellKnownTags.Local => "local",
                    WellKnownTags.Parameter => "parameter",
                    WellKnownTags.Constant => "const",
                    WellKnownTags.EnumMember => "member",
                    WellKnownTags.TypeParameter => "typeparam",
                    _ => string.Empty,
                };
                if (mapped.Length > 0) {
                    return mapped;
                }
            }

            return string.Empty;
        }

        private static string CreateCompletionId(
            RoslynCompletionItem item,
            PromptTextEdit edit,
            string secondaryLabel,
            string trailingLabel) {
            var properties = string.Concat(item.Properties
                .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .Select(static pair => pair.Key + "\n" + pair.Value + "\n"));
            return PromptCompletionItem.CreateId(
                AtelierIds.RoslynLoggerCategory,
                item.DisplayTextPrefix,
                item.DisplayText,
                item.DisplayTextSuffix,
                item.SortText,
                item.FilterText,
                secondaryLabel,
                trailingLabel,
                string.Join("\n", item.Tags.OrderBy(static tag => tag, StringComparer.Ordinal)),
                item.Rules.MatchPriority.ToString(CultureInfo.InvariantCulture),
                edit.NewText,
                edit.StartIndex.ToString(CultureInfo.InvariantCulture),
                edit.Length.ToString(CultureInfo.InvariantCulture),
                properties);
        }

        private readonly record struct CompletionSummary(string Text, PromptHighlightSpan[] Highlights) {
            public static CompletionSummary Empty { get; } = new(string.Empty, []);
        }

    }
}
