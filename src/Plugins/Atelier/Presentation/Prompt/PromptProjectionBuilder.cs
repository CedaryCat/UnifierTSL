using Atelier.Commanding.Meta;
using Atelier.Presentation.Window;
using Atelier.Session;
using Atelier.Session.Roslyn;
using Microsoft.CodeAnalysis.CSharp;
using UnifierTSL.Surface.Prompting.Model;
using UnifierTSL.Surface.Prompting;
using UnifierTSL.Contracts.Display;
using UnifierTSL.Contracts.Projection;
using UnifierTSL.Contracts.Sessions;
using UnifierTSL.Contracts.Terminal;
using UnifierTSL.TextEditing;

namespace Atelier.Presentation.Prompt
{
    internal readonly record struct PromptDocumentRequest(
        PromptInputState InputState,
        long ExpectedClientBufferRevision,
        long RemoteRevision = 0,
        string RequestedCompletionItemId = "",
        ClientBufferedTextMarker[]? Markers = null,
        CSharpParseOptions? ParseOptions = null,
        bool CaptureRawKeys = false,
        ProjectionTextBlock? InputIndicator = null,
        ProjectionTextAnimation? InputIndicatorAnimation = null,
        DraftSnapshot? DraftSnapshot = null,
        bool UseSmartSubmitDetection = false,
        IReadOnlyList<PromptHighlightSpan>? InputHighlights = null,
        EditorSubmitKeyMode SubmitKeyMode = EditorSubmitKeyMode.Enter);

    internal sealed class PromptProjectionBuilder
    {
        private const string ActiveProjectionVariantKey = "active";
        private const string SubmitNotReadyReason = "prompt.submit.not-ready";
        private static readonly ProjectionMarkerCatalogItem[] PairMarkerCatalog = CreatePairMarkerCatalog();

        public ProjectionDocumentContent Build(
            SessionPublication publication,
            PromptDocumentRequest request) {

            var sourceText = publication.SourceText ?? string.Empty;
            var inputState = request.InputState.CopyNormalized(trimPreferredInterpretationId: true, normalizePreferredCompletionText: false);
            var inputText = inputState.InputText ?? string.Empty;
            var parseOptions = request.ParseOptions ?? CSharpParseOptions.Default;
            var draftSnapshot = request.DraftSnapshot
                ?? DraftMarkers.DecodeSnapshot(
                    inputText,
                    NormalizeProjectionMarkers(request.Markers, inputText.Length),
                    inputState.CursorIndex);

            var interpretation = CreateInterpretationProjection(
                publication.Workspace,
                inputText,
                draftSnapshot,
                parseOptions,
                inputState.PreferredInterpretationId);
            var completionItems = ResolveCompletionItems(
                publication.Workspace,
                sourceText,
                inputText,
                inputState.CursorIndex,
                out var activatesCompletion);
            var selectedCompletion = ResolveSelectedCompletion(
                completionItems,
                inputText,
                inputState,
                request.RequestedCompletionItemId,
                publication.Workspace.Completion.PreferredCompletionText);
            var completionActivationMode = activatesCompletion
                || publication.Workspace.Completion.ActivationMode == CompletionTriggerMode.Automatic
                ? CompletionActivationMode.Automatic
                : CompletionActivationMode.Manual;

            var renderOptions = new PromptProjectionRenderOptions(
                EditorPaneKind.MultiLine,
                EditorKeymaps.Create(request.SubmitKeyMode),
                new EditorAuthoringBehavior {
                    OpensCompletionAutomatically = true,
                    CapturesRawKeys = request.CaptureRawKeys,
                    MultilineSubmitMode = request.SubmitKeyMode == EditorSubmitKeyMode.CtrlEnter
                        ? MultilineSubmitMode.AlwaysSubmit
                        : MultilineSubmitMode.UseReadiness,
                },
                interpretation,
                ProjectionZone.Auxiliary,
                ProjectionZone.Auxiliary,
                IncludeStatus: false,
                SuppressCompletionPreview: true,
                CompletionActivationMode: completionActivationMode);

            if (request.InputIndicator is not null || request.InputIndicatorAnimation is not null) {
                renderOptions = renderOptions with {
                    InputIndicator = request.InputIndicator ?? new ProjectionTextBlock(),
                    InputIndicatorAnimation = request.InputIndicatorAnimation,
                };
            }

            var pairProjection = CreatePairProjectionState(draftSnapshot, parseOptions);
            var selectedCompletionId = selectedCompletion?.Id ?? string.Empty;
            var submitSourceText = ResolveSubmitSourceText(publication, request, inputText);
            var isMetaCommand = MetaCommandParser.TryParseSubmittedBuffer(submitSourceText, out _);
            var isReady = isMetaCommand || SyntaxFactory.IsCompleteSubmission(CSharpSyntaxTree.ParseText(submitSourceText ?? string.Empty, parseOptions));
            if (!isMetaCommand
                && isReady
                && request.UseSmartSubmitDetection
                && ShouldExpandPairOnPlainEnter(request.DraftSnapshot, parseOptions)) {
                isReady = false;
            }

            return PromptCanonicalSurfaceSupport.CreateAuthoredContent(
                renderOptions,
                PromptProjectionDocumentFactory.CreateProjectionStyleDictionary(PromptStyles.Default),
                PromptCanonicalSurfaceSupport.CreateBlock(PromptCanonicalSurfaceSupport.CreateStyledSegment(
                    PromptDefaults.Prompt,
                    SurfaceStyleCatalog.PromptLabel)),
                new EditableTextNodeState {
                    BufferText = inputText,
                    CaretIndex = Math.Clamp(inputState.CursorIndex, 0, inputText.Length),
                    ExpectedClientBufferRevision = Math.Max(0, request.ExpectedClientBufferRevision),
                    RemoteRevision = Math.Max(0, request.RemoteRevision),
                    Markers = pairProjection.Markers,
                    Decorations = request.InputHighlights is { Count: > 0 }
                        ? request.InputHighlights
                            .Where(static highlight => highlight.Length > 0
                                && !string.IsNullOrWhiteSpace(highlight.StyleId))
                            .Select(highlight => new ProjectionInlineDecoration {
                                Kind = ProjectionInlineDecorationKind.Highlight,
                                StartIndex = highlight.StartIndex,
                                Length = highlight.Length,
                                Style = ProjectionStyleDictionaryOps.Reference(highlight.StyleId),
                                Content = new ProjectionTextBlock(),
                            })
                            .ToArray()
                        : [],
                    Submit = new ProjectionSubmitState {
                        AcceptsSubmit = true,
                        IsReady = isReady,
                        Reason = isReady ? string.Empty : SubmitNotReadyReason,
                        EmptyInputAction = ProjectionEmptySubmitAction.KeepBuffer,
                        AlternateSubmitBypassesPreview = true,
                    },
                },
                completionItems,
                selectedCompletionId,
                interpretation,
                pairProjection.MarkerCatalog);
        }

        private static PromptInterpretationProjectionOverride CreateInterpretationProjection(
            WorkspaceAnalysis workspace,
            string inputText,
            DraftSnapshot draftSnapshot,
            CSharpParseOptions parseOptions,
            string preferredInterpretationId = "") {
            if (MetaCommandParser.TryParseSubmittedBuffer(inputText, out _)
                || workspace.SignatureHelp.Items.IsDefaultOrEmpty) {
                return new PromptInterpretationProjectionOverride();
            }

            var signatureSourceText = draftSnapshot.SourceText ?? string.Empty;
            var signatureRoot = CSharpSyntaxTree.ParseText(signatureSourceText, parseOptions).GetRoot();
            if (!SignatureHelpLocation.IsActiveLocation(
                signatureSourceText,
                signatureRoot,
                draftSnapshot.SourceCaretIndex)) {
                return new PromptInterpretationProjectionOverride();
            }

            var interpretationState = CreateInterpretationState(workspace.SignatureHelp);
            if (!string.IsNullOrWhiteSpace(preferredInterpretationId)) {
                interpretationState = SelectInterpretation(interpretationState, preferredInterpretationId);
            }

            return CreateInterpretationProjectionOverride(workspace.SignatureHelp, interpretationState);
        }

        private static string ResolveSubmitSourceText(
            SessionPublication publication,
            PromptDocumentRequest request,
            string inputText) {
            if (request.DraftSnapshot is { } draftSnapshot) {
                return draftSnapshot.SourceText;
            }

            return request.Markers is { Length: > 0 }
                ? DraftMarkers.Decode(inputText, request.Markers, request.InputState.CursorIndex).SourceText
                : publication.SourceText ?? string.Empty;
        }

        private static bool ShouldExpandPairOnPlainEnter(DraftSnapshot? draftSnapshot, CSharpParseOptions parseOptions) {
            if (draftSnapshot is null) {
                return false;
            }

            var analysis = VirtualPairAnalyzer.Analyze(draftSnapshot, parseOptions);
            return analysis.ActivePair is { } activePair
                && activePair.Kind is VirtualPairKind.Brace or VirtualPairKind.Bracket
                && IsHorizontalWhitespaceOnly(draftSnapshot.SourceText, activePair.OpenerIndex + 1, activePair.CloserIndex);
        }

        private static bool IsHorizontalWhitespaceOnly(string text, int startIndex, int endIndex) {
            for (var index = Math.Max(0, startIndex); index < Math.Min(endIndex, text.Length); index++) {
                if (text[index] is not (' ' or '\t')) {
                    return false;
                }
            }

            return true;
        }

        private static PromptCompletionItem? ResolveSelectedCompletion(
            IReadOnlyList<PromptCompletionItem> items,
            string inputText,
            PromptInputState inputState,
            string requestedCompletionItemId,
            string defaultPreferredCompletionText) {
            if (!string.IsNullOrWhiteSpace(requestedCompletionItemId)) {
                var preferredById = items.FirstOrDefault(item => string.Equals(item.Id, requestedCompletionItemId, StringComparison.Ordinal));
                if (preferredById is not null) {
                    return preferredById;
                }
            }

            if (!string.IsNullOrWhiteSpace(inputState.PreferredCompletionText)) {
                var preferredByText = ResolvePreferredCompletion(items, inputText, inputState.PreferredCompletionText);
                if (preferredByText is not null) {
                    return preferredByText;
                }
            }

            if (inputState.CompletionIndex > 0 && items.Count > 0) {
                return items[Math.Clamp(inputState.CompletionIndex - 1, 0, items.Count - 1)];
            }

            return ResolvePreferredCompletion(items, inputText, defaultPreferredCompletionText);
        }

        private static PromptCompletionItem? ResolvePreferredCompletion(
            IReadOnlyList<PromptCompletionItem> items,
            string inputText,
            string preferredCompletionText) {
            if (string.IsNullOrWhiteSpace(preferredCompletionText)) {
                return null;
            }

            foreach (var item in items) {
                if (!string.Equals(item.PrimaryEdit.Apply(inputText), preferredCompletionText, StringComparison.OrdinalIgnoreCase)
                    || !PromptInlinePreview.TryCreateInsertions(inputText, item, out _)) {
                    continue;
                }

                return item;
            }

            return null;
        }

        private static IReadOnlyList<PromptCompletionItem> ResolveCompletionItems(
            WorkspaceAnalysis workspace,
            string sourceText,
            string inputText,
            int caretIndex,
            out bool activatesCompletion) {
            activatesCompletion = false;
            if (!string.Equals(sourceText, inputText, StringComparison.Ordinal)) {
                return Array.Empty<PromptCompletionItem>();
            }

            var metaCommandItems = ResolveMetaCommandCompletionItems(sourceText, caretIndex);
            if (metaCommandItems.Count > 0 || MetaCommandParser.TryParseSubmittedBuffer(sourceText, out _)) {
                activatesCompletion = metaCommandItems.Count > 0;
                return metaCommandItems;
            }

            return [.. workspace.Completion.Items];
        }

        private static IReadOnlyList<PromptCompletionItem> ResolveMetaCommandCompletionItems(string sourceText, int caretIndex) {
            if (MetaCommandParser.TryResolveCommandNameToken(
                    sourceText,
                    caretIndex,
                    out var startIndex,
                    out var length,
                    out var prefix)) {
                return ResolveMetaCommandNameCompletionItems(startIndex, length, prefix);
            }

            return MetaCommandParser.TryResolveArgumentToken(sourceText, caretIndex, out var edit)
                ? ResolveMetaCommandArgumentCompletionItems(edit)
                : [];
        }

        private static IReadOnlyList<PromptCompletionItem> ResolveMetaCommandNameCompletionItems(int startIndex, int length, string prefix) {
            List<PromptCompletionItem> items = [];
            var weight = 1000;
            foreach (var command in MetaCommands.All) {
                if (!MatchesPrefix(command.Name, prefix)) {
                    continue;
                }

                items.Add(CreateMetaCommandCompletionItem(command, startIndex, length, weight--));
            }

            return items;
        }

        private static IReadOnlyList<PromptCompletionItem> ResolveMetaCommandArgumentCompletionItems(MetaCommandArgumentEdit edit) {
            List<PromptCompletionItem> items = [];
            var weight = 900;
            foreach (var hint in edit.Command.ArgumentHints ?? []) {
                if (hint.ArgumentIndex != edit.ArgumentIndex || !MatchesPrefix(hint.Value, edit.Prefix)) {
                    continue;
                }

                items.Add(CreateMetaCommandArgumentCompletionItem(edit.Command, hint, edit.StartIndex, edit.Length, weight--));
            }

            return items;
        }

        private static bool MatchesPrefix(string value, string prefix) {
            return !string.IsNullOrWhiteSpace(value)
                && value.StartsWith(prefix ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        private static PromptCompletionItem CreateMetaCommandCompletionItem(
            MetaCommandInfo command,
            int startIndex,
            int length,
            int weight) {
            var insertion = ":" + command.Name;
            var usage = string.IsNullOrWhiteSpace(command.Arguments)
                ? insertion
                : insertion + " " + command.Arguments;
            return new PromptCompletionItem {
                Id = PromptCompletionItem.CreateId("atelier.meta.command", command.Name),
                DisplayText = insertion,
                SecondaryDisplayText = command.Arguments,
                TrailingDisplayText = "command",
                SummaryText = usage + Environment.NewLine + command.Summary,
                DisplayStyleId = PromptStyles.SyntaxControlKeyword,
                SecondaryDisplayStyleId = SurfaceStyleCatalog.CompletionPopupText,
                TrailingDisplayStyleId = SurfaceStyleCatalog.CompletionPopupText,
                SummaryStyleId = PromptStyles.CompletionSummaryText,
                SummaryHighlights = [
                    new PromptHighlightSpan(0, usage.Length, PromptStyles.CompletionSummaryControlKeyword),
                ],
                PreviewStyleId = PromptStyles.GhostControlKeyword,
                PrimaryEdit = new PromptTextEdit(startIndex, length, insertion),
                Weight = weight,
            };
        }

        private static PromptCompletionItem CreateMetaCommandArgumentCompletionItem(
            MetaCommandInfo command,
            MetaCommandArgumentHint hint,
            int startIndex,
            int length,
            int weight) {
            var usage = ":" + command.Name + " " + hint.Value;
            var summary = string.IsNullOrWhiteSpace(hint.Summary)
                ? usage
                : usage + Environment.NewLine + hint.Summary;
            return new PromptCompletionItem {
                Id = PromptCompletionItem.CreateId("atelier.meta.argument", command.Name, hint.ArgumentIndex.ToString(), hint.Value),
                DisplayText = hint.Value,
                SecondaryDisplayText = hint.Summary,
                TrailingDisplayText = "argument",
                SummaryText = summary,
                DisplayStyleId = PromptStyles.SyntaxValue,
                SecondaryDisplayStyleId = SurfaceStyleCatalog.CompletionPopupText,
                TrailingDisplayStyleId = SurfaceStyleCatalog.CompletionPopupText,
                SummaryStyleId = PromptStyles.CompletionSummaryText,
                SummaryHighlights = [
                    new PromptHighlightSpan(0, usage.Length, PromptStyles.CompletionSummaryValue),
                ],
                PreviewStyleId = PromptStyles.GhostValue,
                PrimaryEdit = new PromptTextEdit(startIndex, length, hint.Value),
                Weight = weight,
            };
        }

        private static PairProjectionState CreatePairProjectionState(DraftSnapshot draft, CSharpParseOptions parseOptions) {
            var encoded = DraftMarkers.Encode(draft);
            var encodedDraft = draft.With(
                sourceMarkers: DraftMarkers.CreateSourceMarkers(encoded.PairLedger, draft.SourceText.Length),
                pairLedger: encoded.PairLedger);
            var analysis = VirtualPairAnalyzer.Analyze(encodedDraft, parseOptions);
            var activeMarker = analysis.ActivePair?.Marker;
            return new PairProjectionState(
                [.. encodedDraft.SourceMarkers.Select(marker => new ProjectionTextMarker {
                    Key = marker.BaseKey,
                    VariantKey = activeMarker is not null && marker == activeMarker ? ActiveProjectionVariantKey : string.Empty,
                    StartIndex = marker.EncodedStartIndex,
                    Length = marker.EncodedLength,
                })],
                PairMarkerCatalog);
        }

        private static ProjectionMarkerCatalogItem[] CreatePairMarkerCatalog() {
            List<ProjectionMarkerCatalogItem> items = [];
            foreach (var key in DraftMarkers.Keys) {
                if (!DraftMarkers.TryGetSourceText(key, out var displayText)) {
                    continue;
                }

                items.Add(new ProjectionMarkerCatalogItem {
                    Key = key,
                    VariantKey = string.Empty,
                    DisplayText = displayText,
                    Style = ProjectionStyleDictionaryOps.Reference(ResolvePairMarkerStyleKey(key, active: false)),
                });
                items.Add(new ProjectionMarkerCatalogItem {
                    Key = key,
                    VariantKey = ActiveProjectionVariantKey,
                    DisplayText = displayText,
                    Style = ProjectionStyleDictionaryOps.Reference(ResolvePairMarkerStyleKey(key, active: true)),
                });
            }

            return [.. items];
        }

        private static string ResolvePairMarkerStyleKey(string key, bool active) {
            var quote = DraftMarkers.GetBaseKey(key) is DraftMarkers.DoubleQuoteKey or DraftMarkers.SingleQuoteKey;
            return quote
                ? active ? PromptStyles.PairStringDelimiterActive : PromptStyles.PairStringDelimiter
                : active ? PromptStyles.PairDelimiterActive : PromptStyles.PairDelimiter;
        }

        internal static ClientBufferedTextMarker[] NormalizeProjectionMarkers(IReadOnlyList<ClientBufferedTextMarker>? markers, int textLength) {
            var normalized = EditorTextMarkerOps.Normalize(markers, textLength);
            return [.. normalized.Select(marker => new ClientBufferedTextMarker {
                Key = DraftMarkers.GetBaseKey(marker.Key),
                StartIndex = marker.StartIndex,
                Length = marker.Length,
            })];
        }

        private static PromptInterpretationState CreateInterpretationState(SignatureHelpInfo signatureHelp) {
            if (signatureHelp.Items.IsDefaultOrEmpty) {
                return PromptInterpretationState.Empty;
            }

            var interpretations = new PromptInterpretation[signatureHelp.Items.Length];
            for (var index = 0; index < signatureHelp.Items.Length; index++) {
                var item = signatureHelp.Items[index];
                interpretations[index] = new PromptInterpretation {
                    Id = ResolveItemId(item, index),
                    Summary = item.Summary ?? new PromptStyledText(),
                    Sections = [.. item.Sections.Select(section => new PromptInterpretationSection {
                        Label = section.Label ?? string.Empty,
                        Lines = [.. section.Lines],
                    })],
                };
            }

            var activeIndex = ResolveActiveInterpretationIndex(signatureHelp, interpretations);
            return new PromptInterpretationState {
                Presentation = new PromptInterpretationPresentation {
                    SuppressesCompletionPreview = true,
                    PrefersExpandedDetail = true,
                },
                ActiveInterpretationId = activeIndex >= 0 && activeIndex < interpretations.Length
                    ? interpretations[activeIndex].Id
                    : string.Empty,
                ActiveInterpretationIndex = activeIndex,
                Interpretations = interpretations,
            };
        }

        private readonly record struct PairProjectionState(
            ProjectionTextMarker[] Markers,
            ProjectionMarkerCatalogItem[] MarkerCatalog);

        private static PromptInterpretationState SelectInterpretation(
            PromptInterpretationState interpretationState,
            string preferredInterpretationId) {
            var interpretations = interpretationState.Interpretations ?? [];
            for (var index = 0; index < interpretations.Length; index++) {
                if (!string.Equals(interpretations[index].Id, preferredInterpretationId, StringComparison.Ordinal)) {
                    continue;
                }

                return new PromptInterpretationState {
                    Presentation = interpretationState.Presentation,
                    ActiveInterpretationId = interpretations[index].Id ?? string.Empty,
                    ActiveInterpretationIndex = index,
                    Interpretations = interpretations,
                };
            }

            return interpretationState;
        }

        private static PromptInterpretationProjectionOverride CreateInterpretationProjectionOverride(
            SignatureHelpInfo signatureHelp,
            PromptInterpretationState interpretationState) {
            if (signatureHelp.Items.IsDefaultOrEmpty) {
                return new PromptInterpretationProjectionOverride();
            }

            int activeIndex = ResolveActiveItemIndex(signatureHelp, interpretationState);
            var activeItem = signatureHelp.Items[activeIndex];
            List<InlineSegments> detailLines = [];
            foreach (var section in activeItem.Sections) {
                AppendSectionLines(detailLines, section);
            }

            return new PromptInterpretationProjectionOverride {
                Presentation = interpretationState.Presentation,
                ActiveInterpretationId = ResolveItemId(activeItem, activeIndex),
                Summary = PromptCanonicalSurfaceSupport.CreateStyledSegment(activeItem.Summary),
                DetailLines = [.. detailLines],
                Options = signatureHelp.Items.Length <= 1
                    ? []
                    : [.. signatureHelp.Items
                        .Select((item, index) => new InlineInterpretationOption {
                            Id = ResolveItemId(item, index),
                            Label = new InlineSegments(),
                        })],
            };
        }

        private static void AppendSectionLines(List<InlineSegments> lines, SignatureHelpSection section) {
            var sectionLines = section.Lines;
            if (sectionLines.IsDefaultOrEmpty) {
                return;
            }

            bool suppressLabel = string.IsNullOrWhiteSpace(section.Label)
                || string.Equals(section.Label, "Info", StringComparison.OrdinalIgnoreCase);
            for (var index = 0; index < sectionLines.Length; index++) {
                var line = sectionLines[index];
                if (string.IsNullOrWhiteSpace(line?.Text)) {
                    continue;
                }

                if (index == 0 && !suppressLabel) {
                    lines.Add(PromptCanonicalSurfaceSupport.CreateLabeledStyledSegment(section.Label, line));
                    continue;
                }

                lines.Add(PromptCanonicalSurfaceSupport.CreateStyledSegment(line));
            }
        }

        private static int ResolveActiveItemIndex(SignatureHelpInfo signatureHelp, PromptInterpretationState interpretationState) {
            if (!string.IsNullOrWhiteSpace(interpretationState.ActiveInterpretationId)) {
                for (var index = 0; index < signatureHelp.Items.Length; index++) {
                    if (string.Equals(ResolveItemId(signatureHelp.Items[index], index), interpretationState.ActiveInterpretationId, StringComparison.Ordinal)) {
                        return index;
                    }
                }
            }

            if (interpretationState.ActiveInterpretationIndex >= 0
                && interpretationState.ActiveInterpretationIndex < signatureHelp.Items.Length) {
                return interpretationState.ActiveInterpretationIndex;
            }

            if (!string.IsNullOrWhiteSpace(signatureHelp.ActiveItemId)) {
                for (var index = 0; index < signatureHelp.Items.Length; index++) {
                    if (string.Equals(signatureHelp.Items[index].Id, signatureHelp.ActiveItemId, StringComparison.Ordinal)) {
                        return index;
                    }
                }
            }

            return signatureHelp.ActiveItemIndex >= 0 && signatureHelp.ActiveItemIndex < signatureHelp.Items.Length
                ? signatureHelp.ActiveItemIndex
                : 0;
        }

        private static string ResolveItemId(SignatureHelpItem item, int index) {
            return string.IsNullOrWhiteSpace(item.Id)
                ? $"sig:{index}"
                : item.Id;
        }

        private static int ResolveActiveInterpretationIndex(SignatureHelpInfo signatureHelp, IReadOnlyList<PromptInterpretation> interpretations) {
            if (!string.IsNullOrWhiteSpace(signatureHelp.ActiveItemId)) {
                for (var index = 0; index < interpretations.Count; index++) {
                    if (string.Equals(interpretations[index].Id, signatureHelp.ActiveItemId, StringComparison.Ordinal)) {
                        return index;
                    }
                }
            }

            return signatureHelp.ActiveItemIndex >= 0 && signatureHelp.ActiveItemIndex < interpretations.Count
                ? signatureHelp.ActiveItemIndex
                : interpretations.Count == 0 ? -1 : 0;
        }

    }
}
