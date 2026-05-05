using System.Text;
using UnifierTSL.Surface.Prompting;
using UnifierTSL.Commanding;
using UnifierTSL.Surface.Prompting.Model;

namespace UnifierTSL.Surface.Prompting.Semantics;

/*
    PromptSemanticCompletion is the projection layer between semantic analysis and visible UX.

    Two rules here are easy to break during cleanup:
    1. Provider candidates are not always filtered by plain StartsWith. Some domains supply
       extra matcher semantics because the runtime binder does more than prefix matching.
    2. Slot edits must preserve prompt/parser parity. If a completion needs quotes or escapes,
       we must emit them here so ghost preview, accepted text, and final execution all describe
       the same token sequence.

    Be conservative when changing this file. Reverting provider-aware matching or quote-aware
    text edits causes subtle regressions that look like "ghost picked the wrong path" even
    though the real bug is prompt/runtime semantic drift.

    Preserve nearby comments unless the mechanism changes. If a mechanism changes, update the
    comment in the same diff to document the new invariant and its failure mode.
*/
internal static class PromptSemanticCompletion
{

    public static IReadOnlyList<PromptCompletionItem> ResolvePlainSuggestions(
        string? input,
        ResolvedPrompt context) {
        var rawText = input ?? string.Empty;
        var source = context.Candidates.Values.SelectMany(static items => items);
        return BuildCompletionItems(
            PromptSuggestionOps.OrderUniqueByWeight(source)
                .Where(suggestion => MatchesPrefix(suggestion.Value, rawText))
                .Select(suggestion => new PromptCompletionItem {
                    Id = PromptCompletionItem.CreateId("plain", suggestion.Value),
                    DisplayText = suggestion.Value,
                    DisplayStyleId = PromptStyleKeys.SyntaxValue,
                    PreviewStyleId = PromptStyleKeys.GhostValue,
                    Weight = suggestion.Weight,
                    PrimaryEdit = new PromptTextEdit(0, rawText.Length, suggestion.Value),
                }));
    }

    public static IReadOnlyList<PromptCompletionItem> ResolveSemanticSuggestions(
        ResolvedPrompt context,
        PromptSemanticAnalysis analysis,
        PromptInputState state,
        PromptSurfaceScenario scenario) {
        List<PromptCompletionItem> results = [];
        switch (analysis.FocusKind) {
            case PromptFocusKind.Literal:
                results.AddRange(analysis.AvailableLiterals
                    .Where(expected => MatchesPrefix(expected, analysis.EditTarget.RawText))
                    .Select(expected => CreateTokenCompletion(
                        expected,
                        analysis.EditTarget,
                        PromptStyleKeys.SyntaxKeyword,
                        PromptStyleKeys.GhostKeyword,
                        weight: ResolvePrefixMatchWeight(expected, analysis.EditTarget.RawText, 120))));
                break;

            case PromptFocusKind.Modifier:
                if (analysis.AcceptExpectation.Kind == PromptFocusKind.Modifier) {
                    results.AddRange(analysis.AvailableModifiers
                        .Where(modifier => modifier.Tokens.Any(token => MatchesPrefix(token, analysis.EditTarget.RawText)))
                        .Select(modifier => CreateTokenCompletion(
                            modifier.CanonicalToken,
                            analysis.EditTarget,
                            PromptStyleKeys.SyntaxModifier,
                            PromptStyleKeys.GhostModifier,
                            weight: ResolvePrefixMatchWeight(modifier.CanonicalToken, analysis.EditTarget.RawText, 160),
                            secondaryDisplayText: modifier.DisplayLabel)));
                }
                break;

            case PromptFocusKind.Slot:
                results.AddRange(ResolveSlotSuggestions(context, analysis, state, scenario));
                break;
        }

        return BuildCompletionItems(results);
    }

    public static PromptSemanticAnalysis ResolvePresentationAnalysis(
        ResolvedPrompt context,
        PromptSemanticAnalysis committedAnalysis,
        IReadOnlyList<PromptCompletionItem> suggestions,
        PromptInputState state,
        PromptSurfaceScenario scenario) {
        var committedInput = state.InputText ?? string.Empty;
        var presentationInput = ResolvePresentationInputText(committedInput, suggestions, state);
        if (string.Equals(presentationInput, committedInput, StringComparison.Ordinal)) {
            return committedAnalysis;
        }

        return PromptSemanticAnalyzer.TryAnalyze(context, state, scenario, presentationInput) ?? committedAnalysis;
    }

    public static string ResolvePresentationInputText(
        string currentText,
        IReadOnlyList<PromptCompletionItem> suggestions,
        PromptInputState state) {
        if (!TryResolvePresentationInput(currentText, suggestions, state, out var presentationInput)) {
            return currentText;
        }

        return presentationInput;
    }

    public static PromptInterpretationState BuildInterpretationState(
        ResolvedPrompt context,
        PromptSemanticAnalysis analysis,
        PromptSemanticAnalysis presentationAnalysis,
        PromptInputState state,
        PromptSurfaceScenario scenario,
        IReadOnlyList<string> baseStatusBodyLines,
        out IReadOnlyList<string> statusBodyLines) {
        var interpretationState = ResolveVisibleInterpretationState(analysis, presentationAnalysis);
        var detailAnalysis = ResolveInterpretationDetailAnalysis(analysis, presentationAnalysis, interpretationState);
        PromptInterpretation[] interpretations = interpretationState.Interpretations ?? [];
        var visibleStatusBodyLines = BuildVisibleStatusBodyLines(baseStatusBodyLines, interpretations.Length > 1);
        int activeInterpretationIndex = ResolveActiveInterpretationIndex(interpretationState, interpretations);
        if (activeInterpretationIndex < 0 || activeInterpretationIndex >= interpretations.Length) {
            statusBodyLines = BuildSemanticStatusLines(visibleStatusBodyLines, detailAnalysis.Diagnostics);
            return interpretationState;
        }

        IReadOnlyList<string> remainingDiagnostics = detailAnalysis.Diagnostics;
        interpretations = [.. interpretations];
        PromptInterpretation activeInterpretation = interpretations[activeInterpretationIndex];
        interpretations[activeInterpretationIndex] = new PromptInterpretation {
            Id = activeInterpretation.Id,
            Label = activeInterpretation.Label,
            Summary = BuildInterpretationSummary(detailAnalysis, activeInterpretation),
            Sections = BuildInterpretationSections(context, detailAnalysis, state, scenario, out remainingDiagnostics),
        };
        statusBodyLines = BuildSemanticStatusLines(visibleStatusBodyLines, remainingDiagnostics);
        return new PromptInterpretationState {
            ActiveInterpretationId = interpretationState.ActiveInterpretationId,
            ActiveInterpretationIndex = activeInterpretationIndex,
            Interpretations = interpretations,
        };
    }

    private static IReadOnlyList<string> BuildVisibleStatusBodyLines(
        IReadOnlyList<string> baseStatusBodyLines,
        bool includeInterpretationSwitchHint) {
        if (baseStatusBodyLines.Count == 0 || !LooksLikeCommandAssistHintLine(baseStatusBodyLines[0])) {
            return baseStatusBodyLines;
        }

        var assistHintLine = PromptEditorKeymaps.CreateCommandAssistHintLine(includeInterpretationSwitchHint);
        if (string.Equals(baseStatusBodyLines[0], assistHintLine, StringComparison.Ordinal)) {
            return baseStatusBodyLines;
        }

        List<string> rewritten = [assistHintLine];
        rewritten.AddRange(baseStatusBodyLines.Skip(1));
        return rewritten;
    }

    private static bool LooksLikeCommandAssistHintLine(string? line) {
        return !string.IsNullOrWhiteSpace(line)
            && line.Contains("rotates suggestions", StringComparison.OrdinalIgnoreCase)
            && line.Contains("accepts ghost completion", StringComparison.OrdinalIgnoreCase);
    }

    private static PromptInterpretationState ResolveVisibleInterpretationState(
        PromptSemanticAnalysis analysis,
        PromptSemanticAnalysis presentationAnalysis) {
        // Ghost/presentation analysis may fully commit a single branch before the operator has
        // accepted that edit. Keep the raw chooser visible while the committed input is still
        // ambiguous, otherwise Shift+Tab loses the alternate overload too early.
        return (analysis.InterpretationState.Interpretations ?? []).Length > 1
            ? analysis.InterpretationState
            : presentationAnalysis.InterpretationState;
    }

    private static PromptSemanticAnalysis ResolveInterpretationDetailAnalysis(
        PromptSemanticAnalysis analysis,
        PromptSemanticAnalysis presentationAnalysis,
        PromptInterpretationState interpretationState) {
        if ((interpretationState.Interpretations ?? []).Length <= 1) {
            return presentationAnalysis;
        }

        var activeInterpretationId = ResolveActiveInterpretationId(interpretationState);
        if (string.IsNullOrWhiteSpace(activeInterpretationId)) {
            return analysis;
        }

        return string.Equals(
                ResolveActiveInterpretationId(presentationAnalysis.InterpretationState),
                activeInterpretationId,
                StringComparison.Ordinal)
            ? presentationAnalysis
            : analysis;
    }

    private static int ResolveActiveInterpretationIndex(PromptInterpretationState interpretationState, IReadOnlyList<PromptInterpretation> interpretations) {
        if (!string.IsNullOrWhiteSpace(interpretationState.ActiveInterpretationId)) {
            for (var index = 0; index < interpretations.Count; index++) {
                if (string.Equals(interpretations[index].Id, interpretationState.ActiveInterpretationId, StringComparison.Ordinal)) {
                    return index;
                }
            }
        }

        return interpretationState.ActiveInterpretationIndex >= 0 && interpretationState.ActiveInterpretationIndex < interpretations.Count
            ? interpretationState.ActiveInterpretationIndex
            : interpretations.Count == 0 ? -1 : 0;
    }

    private static string ResolveActiveInterpretationId(PromptInterpretationState interpretationState) {
        if (!string.IsNullOrWhiteSpace(interpretationState.ActiveInterpretationId)) {
            return interpretationState.ActiveInterpretationId;
        }

        var interpretations = interpretationState.Interpretations ?? [];
        return interpretationState.ActiveInterpretationIndex >= 0 && interpretationState.ActiveInterpretationIndex < interpretations.Length
            ? interpretations[interpretationState.ActiveInterpretationIndex].Id ?? string.Empty
            : string.Empty;
    }

    public static bool TryCreateExplainContext(
        ResolvedPrompt context,
        PromptSemanticAnalysis analysis,
        PromptInputState state,
        PromptSurfaceScenario scenario,
        out PromptParamExplainContext explainContext) {
        explainContext = default;
        if (analysis.ActiveSlot is null || analysis.ActiveSlot.SemanticKey is null) {
            return false;
        }

        explainContext = new PromptParamExplainContext(
            ResolveContext: new PromptResolveContext(context.Purpose, state, scenario),
            Server: context.Server,
            ActiveAlternative: analysis.ActiveAlternative,
            ActiveSlot: analysis.ActiveSlot,
            EditTarget: analysis.EditTarget,
            RawText: analysis.EditTarget.RawText);
        return true;
    }

    public static bool TryCreateCandidateContext(
        ResolvedPrompt context,
        PromptSemanticAnalysis analysis,
        PromptInputState state,
        PromptSurfaceScenario scenario,
        out PromptParamCandidateContext candidateContext) {
        candidateContext = default;
        if (analysis.ActiveSlot is null || analysis.ActiveSlot.SemanticKey is null) {
            return false;
        }

        candidateContext = new PromptParamCandidateContext(
            ResolveContext: new PromptResolveContext(context.Purpose, state, scenario),
            Server: context.Server,
            ActiveAlternative: analysis.ActiveAlternative,
            ActiveSlot: analysis.ActiveSlot,
            EditTarget: analysis.EditTarget,
            RawText: analysis.EditTarget.RawText,
            RawInputText: analysis.SlotInputTarget.RawText);
        return true;
    }

    private static IEnumerable<PromptCompletionItem> ResolveSlotSuggestions(
        ResolvedPrompt context,
        PromptSemanticAnalysis analysis,
        PromptInputState state,
        PromptSurfaceScenario scenario) {
        if (analysis.ActiveSlot is null) {
            return [];
        }

        var rawText = analysis.EditTarget.RawText;
        List<PromptCompletionItem> completions = [];

        foreach (var accepted in analysis.ActiveSlot.AcceptedSpecialTokens) {
            AddDefaultSlotCompletion(completions, accepted, analysis.EditTarget, rawText, weight: 160);
        }

        foreach (var candidate in analysis.ActiveSlot.EnumCandidates) {
            AddDefaultSlotCompletion(completions, candidate, analysis.EditTarget, rawText, weight: 140);
        }

        foreach (var suggestion in context.ResolveCandidates(analysis.ActiveSlot.CompletionKindId)) {
            AddDefaultSlotCompletion(completions, suggestion.Value, analysis.EditTarget, rawText, suggestion.Weight);
        }

        if (analysis.ActiveSlot.SemanticKey is SemanticKey semanticKey
            && context.ResolveParameterCandidateProvider(semanticKey) is IParamValueCandidateProvider provider) {
            PromptParamCandidateContext providerContext = new(
                ResolveContext: new PromptResolveContext(context.Purpose, state, scenario),
                Server: context.Server,
                ActiveAlternative: analysis.ActiveAlternative,
                ActiveSlot: analysis.ActiveSlot,
                EditTarget: analysis.EditTarget,
                RawText: rawText,
                RawInputText: analysis.SlotInputTarget.RawText);

            try {
                foreach (var candidate in provider.GetCandidates(providerContext) ?? []) {
                    AddProviderSlotCompletion(completions, provider, providerContext, candidate, analysis.EditTarget, weight: 120);
                }
            }
            catch {
            }
        }

        return completions;
    }

    private static void AddDefaultSlotCompletion(
        List<PromptCompletionItem> completions,
        string value,
        PromptEditTarget target,
        string rawText,
        int weight) {
        if (string.IsNullOrWhiteSpace(value) || !MatchesPrefix(value, rawText)) {
            return;
        }

        completions.Add(CreateSlotCompletion(
            value,
            target,
            ResolvePrefixMatchWeight(value, rawText, weight)));
    }

    private static void AddProviderSlotCompletion(
        List<PromptCompletionItem> completions,
        IParamValueCandidateProvider provider,
        PromptParamCandidateContext context,
        string value,
        PromptEditTarget target,
        int weight) {
        if (string.IsNullOrWhiteSpace(value)) {
            return;
        }

        // Do not fold provider candidates back into plain StartsWith filtering. Some providers
        // intentionally mirror richer runtime semantics, and dropping that hook reintroduces
        // prompt-only regressions such as wildcard-style matches disappearing from ghost text.
        var matchWeight = provider is IParamValueCandidateMatcher matcher
            ? matcher.ResolveMatchWeight(context, value, weight)
            : MatchesPrefix(value, context.RawText)
                ? ResolvePrefixMatchWeight(value, context.RawText, weight)
                : null;
        if (matchWeight is not int resolvedWeight) {
            return;
        }

        completions.Add(CreateSlotCompletion(value, target, resolvedWeight));
    }

    private static PromptCompletionItem CreateSlotCompletion(
        string value,
        PromptEditTarget target,
        int weight) {
        return new PromptCompletionItem {
            Id = PromptCompletionItem.CreateId("slot", value),
            DisplayText = value,
            DisplayStyleId = PromptStyleKeys.SyntaxValue,
            PreviewStyleId = PromptStyleKeys.GhostValue,
            Weight = weight,
            PrimaryEdit = CreateSlotEdit(value, target),
        };
    }

    private static PromptCompletionItem CreateTokenCompletion(
        string value,
        PromptEditTarget target,
        string displayStyleId,
        string previewStyleId,
        int weight,
        string? displayText = null,
        string? secondaryDisplayText = null) {
        return new PromptCompletionItem {
            Id = PromptCompletionItem.CreateId("token", value, displayText, secondaryDisplayText),
            DisplayText = displayText ?? value,
            SecondaryDisplayText = secondaryDisplayText ?? string.Empty,
            DisplayStyleId = displayStyleId ?? string.Empty,
            PreviewStyleId = previewStyleId ?? string.Empty,
            Weight = weight,
            PrimaryEdit = new PromptTextEdit(target.StartIndex, target.Length, value),
        };
    }

    private static IReadOnlyList<PromptCompletionItem> BuildCompletionItems(IEnumerable<PromptCompletionItem> source) {
        return [.. source
            .Where(static item => item is not null && !string.IsNullOrWhiteSpace(item.DisplayText))
            .GroupBy(static item => string.IsNullOrWhiteSpace(item.Id) ? item.DisplayText : item.Id, StringComparer.Ordinal)
            .Select(group => group
                .OrderByDescending(static item => item.Weight)
                .ThenBy(static item => item.DisplayText, PromptSuggestionOps.DisplayTextComparer)
                .First())
            .OrderByDescending(static item => item.Weight)
            .ThenBy(static item => item.DisplayText, PromptSuggestionOps.DisplayTextComparer)];
    }

    private static bool TryResolvePresentationInput(
        string currentText,
        IReadOnlyList<PromptCompletionItem> suggestions,
        PromptInputState state,
        out string presentationInput) {
        presentationInput = currentText;
        if (state.CursorIndex != currentText.Length) {
            return false;
        }

        var candidate = ResolvePresentationCandidate(suggestions, currentText, state);
        if (candidate is null || !PromptInlinePreview.TryCreateInsertions(currentText, candidate, out _)) {
            return false;
        }

        presentationInput = candidate.PrimaryEdit.Apply(currentText);
        return true;
    }

    private static PromptCompletionItem? ResolvePresentationCandidate(
        IReadOnlyList<PromptCompletionItem> suggestions,
        string currentText,
        PromptInputState state) {
        if (suggestions.Count == 0) {
            return null;
        }

        var selectedOrdinal = state.CompletionIndex;
        if (selectedOrdinal > 0) {
            var selectedIndex = selectedOrdinal - 1;
            return selectedIndex >= 0 && selectedIndex < suggestions.Count
                ? suggestions[selectedIndex]
                : null;
        }

        if (!string.IsNullOrWhiteSpace(state.PreferredCompletionText)) {
            var preferred = suggestions.FirstOrDefault(candidate =>
                string.Equals(
                    candidate.PrimaryEdit.Apply(currentText),
                    state.PreferredCompletionText,
                    StringComparison.OrdinalIgnoreCase));
            if (preferred is not null) {
                return preferred;
            }
        }

        return suggestions[0];
    }

    private static bool MatchesPrefix(string candidate, string rawText) {
        if (string.IsNullOrEmpty(rawText)) {
            return true;
        }

        return candidate.StartsWith(rawText, StringComparison.OrdinalIgnoreCase);
    }

    private static int ResolvePrefixMatchWeight(string candidate, string rawText, int baseWeight) {
        if (string.IsNullOrEmpty(rawText)) {
            return baseWeight;
        }

        if (candidate.Equals(rawText, StringComparison.OrdinalIgnoreCase)) {
            return baseWeight + 1000;
        }

        if (!candidate.StartsWith(rawText, StringComparison.OrdinalIgnoreCase)) {
            return baseWeight;
        }

        var remainingLength = Math.Max(0, candidate.Length - rawText.Length);
        return baseWeight + Math.Max(1, 256 - Math.Min(255, remainingLength));
    }

    private static PromptTextEdit CreateSlotEdit(string value, PromptEditTarget target) {
        // Quote/escape decisions here must stay aligned with PromptInputLexer and the runtime
        // command parser. If we emit raw spaced text here, `"fallen star"`-style completions
        // will preview or accept into a token stream that execution does not actually see.
        //
        // Multi-token routes are the exception: RemainingText/Variadic/GreedyPhrase slots already
        // own embedded spaces at the route level, so wrapping those suggestions in quotes would
        // duplicate input and collapse continuation edits back into "replace only the last token".
        if (target.AllowUnquotedWhitespace
            && target.Length < target.RawText.Length
            && TryCreateContinuationEdit(value, target, out var continuationEdit)) {
            return continuationEdit;
        }

        if (target.Quoted || target.HasLeadingQuote || target.HasTrailingQuote) {
            var start = Math.Max(0, target.StartIndex - (target.HasLeadingQuote ? 1 : 0));
            var length = target.Length + (target.HasLeadingQuote ? 1 : 0) + (target.HasTrailingQuote ? 1 : 0);
            return new PromptTextEdit(start, length, $"\"{EscapeQuoted(value)}\"");
        }

        if (!target.AllowUnquotedWhitespace && NeedsQuotedWrapper(value)) {
            return new PromptTextEdit(target.StartIndex, target.Length, $"\"{EscapeQuoted(value)}\"");
        }

        if (target.LeadingCharacterEscaped && value.StartsWith("-", StringComparison.Ordinal)) {
            return new PromptTextEdit(target.StartIndex, target.Length, "\\" + value);
        }

        return new PromptTextEdit(target.StartIndex, target.Length, value);
    }

    private static bool TryCreateContinuationEdit(string value, PromptEditTarget target, out PromptTextEdit edit) {
        edit = default;
        if (string.IsNullOrEmpty(value)
            || string.IsNullOrEmpty(target.RawText)
            || !value.StartsWith(target.RawText, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        var prefixLength = ResolveContinuationPrefixLength(target.RawText);
        if (prefixLength <= 0 || prefixLength > value.Length) {
            return false;
        }

        edit = new PromptTextEdit(target.StartIndex, target.Length, value[prefixLength..]);
        return true;
    }

    private static int ResolveContinuationPrefixLength(string rawText) {
        if (string.IsNullOrEmpty(rawText)) {
            return 0;
        }

        var separatorIndex = rawText.LastIndexOf(' ');
        return separatorIndex < 0
            ? 0
            : separatorIndex + 1;
    }

    private static bool NeedsQuotedWrapper(string value) {
        return value.Any(static ch => char.IsWhiteSpace(ch) || ch == '"');
    }

    private static string EscapeQuoted(string value) {
        return (value ?? string.Empty)
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private static string ResolveExpectationLabel(PromptSemanticSpec? spec, PromptSemanticAnalysis analysis) {
        return analysis.AcceptExpectation.Kind switch {
            PromptFocusKind.Literal => string.IsNullOrWhiteSpace(spec?.LiteralExpectationLabel)
                ? "literal"
                : spec!.LiteralExpectationLabel.Trim(),
            PromptFocusKind.Slot when analysis.AcceptExpectation.Slot is PromptSlotSegmentSpec slot => ResolveSlotLabel(
                slot,
                ResolveDisplaySlotOrdinal(analysis.AcceptExpectation.SegmentIndex, analysis.ActiveAlternative)),
            PromptFocusKind.Modifier => string.IsNullOrWhiteSpace(spec?.ModifierLabel)
                ? "modifier"
                : spec!.ModifierLabel.Trim(),
            PromptFocusKind.Overflow or PromptFocusKind.None => string.IsNullOrWhiteSpace(spec?.OverflowExpectationLabel)
                ? "no more input"
                : spec!.OverflowExpectationLabel.Trim(),
            _ => string.Empty,
        };
    }

    private static IReadOnlyList<string> BuildSemanticStatusLines(
        IReadOnlyList<string> baseStatusBodyLines,
        IReadOnlyList<string> diagnostics) {
        List<string> lines = [.. baseStatusBodyLines];
        lines.AddRange(diagnostics.Where(static diagnostic => !string.IsNullOrWhiteSpace(diagnostic)));
        return lines;
    }

    private static PromptStyledText BuildInterpretationSummary(PromptSemanticAnalysis analysis, PromptInterpretation interpretation) {
        string title = analysis.ActiveAlternative.Title?.Trim() ?? string.Empty;
        string summary = analysis.ActiveAlternative.Summary?.Trim() ?? string.Empty;
        string text = !string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(summary)
            ? $"{title} | {summary}"
            : !string.IsNullOrWhiteSpace(title)
                ? title
                : !string.IsNullOrWhiteSpace(summary)
                    ? summary
                    : interpretation.Label ?? string.Empty;
        return CreateStyledText(text);
    }

    private static PromptInterpretationSection[] BuildInterpretationSections(
        ResolvedPrompt context,
        PromptSemanticAnalysis analysis,
        PromptInputState state,
        PromptSurfaceScenario scenario,
        out IReadOnlyList<string> remainingDiagnostics) {
        List<PromptInterpretationSection> sections = [];
        PromptStyledText expectation = BuildExpectationSectionText(context.SemanticSpec, analysis);
        if (!string.IsNullOrWhiteSpace(expectation.Text)) {
            sections.Add(new PromptInterpretationSection {
                Label = "expect",
                Lines = [expectation],
            });
        }

        PromptStyledText resolution = BuildActiveResolutionSectionText(context, analysis, state, scenario, analysis.Diagnostics, out remainingDiagnostics);
        if (!string.IsNullOrWhiteSpace(resolution.Text)) {
            sections.Add(new PromptInterpretationSection {
                Label = "resolved",
                Lines = [resolution],
            });
        }

        return [.. sections];
    }

    private static PromptStyledText BuildExpectationSectionText(PromptSemanticSpec? spec, PromptSemanticAnalysis analysis) {
        string expectationLabel = ResolveExpectationLabel(spec, analysis);
        PromptStyledText argumentShape = BuildArgumentShapeText(analysis);
        string flagSuffix = ResolveRecognizedFlagText(analysis);
        if (string.IsNullOrWhiteSpace(expectationLabel) && string.IsNullOrWhiteSpace(argumentShape.Text)) {
            return CreateStyledText(flagSuffix.TrimStart());
        }

        return ComposeStyledText(
            !string.IsNullOrWhiteSpace(expectationLabel)
                ? expectationLabel + (string.IsNullOrWhiteSpace(argumentShape.Text) ? string.Empty : " | ")
                : string.Empty,
            argumentShape,
            flagSuffix);
    }

    private static PromptStyledText BuildArgumentShapeText(PromptSemanticAnalysis analysis) {
        if (!ShouldRenderArgumentShape(analysis)) {
            return CreateStyledText(string.Empty);
        }

        var segments = ResolveDisplaySegments(analysis);
        if (segments.Count == 0) {
            return CreateStyledText(string.Empty);
        }

        var tokens = BuildArgumentDisplayTokens(segments);
        string text = string.Join(' ', tokens.Select(static token => token.Text));
        int highlightIndex = ResolveHighlightTokenIndex(tokens, analysis);
        if (highlightIndex < 0) {
            return CreateStyledText(text);
        }

        int startIndex = 0;
        for (int index = 0; index < highlightIndex; index++) {
            startIndex += tokens[index].Text.Length + 1;
        }

        string highlightedText = tokens[highlightIndex].Text;
        return new PromptStyledText {
            Text = text,
            Highlights = highlightedText.Length == 0
                ? []
                : [new PromptHighlightSpan(startIndex, highlightedText.Length, PromptStyleKeys.SyntaxModifier)],
        };
    }

    private static bool ShouldRenderArgumentShape(PromptSemanticAnalysis analysis) {
        // The route tail is only meaningful after the root command literal has committed.
        // While the caret is still inside the command token, showing downstream args makes
        // the status read as if we had already advanced into the first parameter slot.
        return analysis.AcceptExpectation.Kind != PromptFocusKind.Literal
            || analysis.AcceptExpectation.SegmentIndex > 0;
    }

    private static List<DisplaySegmentDescriptor> ResolveDisplaySegments(PromptSemanticAnalysis analysis) {
        IReadOnlyList<PromptAlternativeSpec> compatible = analysis.ActiveInterpretationAlternatives
            .Where(alternative => alternative.DisplayGroupKey.Equals(analysis.ActiveAlternative.DisplayGroupKey, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (compatible.Count == 0) {
            compatible = [analysis.ActiveAlternative];
        }

        var activeSegments = BuildDisplaySegments(analysis.ActiveAlternative);
        if (compatible.Count == 1) {
            return activeSegments;
        }

        return TryMergeCompatibleAlternatives(compatible, out var merged)
            ? merged
            : activeSegments;
    }

    private static string ResolveRecognizedFlagText(PromptSemanticAnalysis analysis) {
        if (analysis.RecognizedFlags.Length == 0) {
            return string.Empty;
        }

        var content = string.Join(',', analysis.RecognizedFlags
            .Select(static flag => ResolveModifierDisplayLabel(flag))
            .Where(static label => !string.IsNullOrWhiteSpace(label))
            .Distinct(StringComparer.OrdinalIgnoreCase));
        return string.IsNullOrWhiteSpace(content)
            ? string.Empty
            : $" - flag[{content}]";
    }

    private static List<DisplaySegmentDescriptor> BuildDisplaySegments(PromptAlternativeSpec alternative) {
        List<DisplaySegmentDescriptor> segments = [];
        for (var index = 1; index < alternative.Segments.Length; index++) {
            segments.Add(new DisplaySegmentDescriptor(index, alternative.Segments[index]));
        }

        return segments;
    }

    private static bool TryMergeCompatibleAlternatives(
        IReadOnlyList<PromptAlternativeSpec> alternatives,
        out List<DisplaySegmentDescriptor> merged) {
        merged = BuildDisplaySegments(alternatives[0]);
        for (var alternativeIndex = 1; alternativeIndex < alternatives.Count; alternativeIndex++) {
            var current = BuildDisplaySegments(alternatives[alternativeIndex]);
            var shared = Math.Min(merged.Count, current.Count);
            for (var index = 0; index < shared; index++) {
                if (!CanMergeDisplaySegment(merged[index].Segment, current[index].Segment)) {
                    merged = [];
                    return false;
                }

                merged[index] = new DisplaySegmentDescriptor(
                    merged[index].SegmentIndex,
                    MergeDisplaySegment(merged[index].Segment, current[index].Segment));
            }

            if (current.Count > merged.Count) {
                for (var index = merged.Count; index < current.Count; index++) {
                    if (!TryMakeOptional(current[index].Segment, out var optionalized)) {
                        merged = [];
                        return false;
                    }

                    merged.Add(new DisplaySegmentDescriptor(current[index].SegmentIndex, optionalized));
                }
            }
            else if (merged.Count > current.Count) {
                for (var index = current.Count; index < merged.Count; index++) {
                    if (!TryMakeOptional(merged[index].Segment, out var optionalized)) {
                        merged = [];
                        return false;
                    }

                    merged[index] = new DisplaySegmentDescriptor(merged[index].SegmentIndex, optionalized);
                }
            }
        }

        return true;
    }

    private static bool CanMergeDisplaySegment(PromptSegmentSpec left, PromptSegmentSpec right) {
        if (left is PromptLiteralSegmentSpec leftLiteral && right is PromptLiteralSegmentSpec rightLiteral) {
            return leftLiteral.Value.Equals(rightLiteral.Value, StringComparison.OrdinalIgnoreCase);
        }

        if (left is not PromptSlotSegmentSpec leftSlot || right is not PromptSlotSegmentSpec rightSlot) {
            return false;
        }

        return ResolveSlotLabel(leftSlot, fallbackIndex: 0, preferArgFallback: false)
                .Equals(ResolveSlotLabel(rightSlot, fallbackIndex: 0, preferArgFallback: false), StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                leftSlot.CompletionKindId?.Trim() ?? string.Empty,
                rightSlot.CompletionKindId?.Trim() ?? string.Empty,
                StringComparison.Ordinal)
            && leftSlot.ValidationMode == rightSlot.ValidationMode
            && leftSlot.EnumCandidates.SequenceEqual(rightSlot.EnumCandidates, StringComparer.OrdinalIgnoreCase)
            && leftSlot.AcceptedSpecialTokens.SequenceEqual(rightSlot.AcceptedSpecialTokens, StringComparer.OrdinalIgnoreCase)
            && PromptSlotMetadata.ContentEquals(leftSlot.Metadata, rightSlot.Metadata)
            && (leftSlot.Cardinality == rightSlot.Cardinality
                || (leftSlot.Cardinality is PromptSlotCardinality.Required or PromptSlotCardinality.Optional
                    && rightSlot.Cardinality is PromptSlotCardinality.Required or PromptSlotCardinality.Optional));
    }

    private static PromptSegmentSpec MergeDisplaySegment(PromptSegmentSpec left, PromptSegmentSpec right) {
        if (left is PromptSlotSegmentSpec leftSlot && right is PromptSlotSegmentSpec rightSlot) {
            var cardinality = leftSlot.Cardinality == PromptSlotCardinality.Optional
                || rightSlot.Cardinality == PromptSlotCardinality.Optional
                    ? PromptSlotCardinality.Optional
                    : leftSlot.Cardinality;
            return leftSlot with { Cardinality = cardinality };
        }

        return left;
    }

    private static bool TryMakeOptional(PromptSegmentSpec segment, out PromptSegmentSpec optionalized) {
        if (segment is not PromptSlotSegmentSpec slot) {
            optionalized = default!;
            return false;
        }

        optionalized = slot with { Cardinality = PromptSlotCardinality.Optional };
        return true;
    }

    private static List<ArgumentDisplayToken> BuildArgumentDisplayTokens(IReadOnlyList<DisplaySegmentDescriptor> segments) {
        List<ArgumentDisplayToken> tokens = [];
        var slotOrdinal = 0;
        foreach (var descriptor in segments) {
            switch (descriptor.Segment) {
                case PromptLiteralSegmentSpec literal:
                    if (!string.IsNullOrWhiteSpace(literal.Value)) {
                        tokens.Add(new ArgumentDisplayToken(
                            literal.Value.Trim(),
                            IsVariadic: false,
                            descriptor.SegmentIndex));
                    }
                    break;

                case PromptSlotSegmentSpec slot:
                    var parameterName = ResolveSlotLabel(slot, slotOrdinal);
                    if (slot.AcceptedSpecialTokens.Length > 0) {
                        parameterName = string.Join('|', [parameterName, .. slot.AcceptedSpecialTokens]);
                    }

                    if (slot.Cardinality == PromptSlotCardinality.Variadic && !parameterName.EndsWith("...", StringComparison.Ordinal)) {
                        parameterName += "...";
                    }

                    var wrapped = slot.Cardinality == PromptSlotCardinality.Optional
                        ? "[" + parameterName + "]"
                        : "<" + parameterName + ">";
                    tokens.Add(new ArgumentDisplayToken(
                        wrapped,
                        IsVariadic: slot.Cardinality == PromptSlotCardinality.Variadic,
                        descriptor.SegmentIndex));
                    slotOrdinal += 1;
                    break;
            }
        }

        return tokens;
    }

    private static int ResolveHighlightTokenIndex(
        IReadOnlyList<ArgumentDisplayToken> tokens,
        PromptSemanticAnalysis analysis) {
        if (analysis.FocusKind != PromptFocusKind.Slot || analysis.ActiveSegmentIndex < 0) {
            return -1;
        }

        for (var index = 0; index < tokens.Count; index++) {
            if (tokens[index].SegmentIndex == analysis.ActiveSegmentIndex) {
                return index;
            }
        }

        return -1;
    }

    private static int ResolveDisplaySlotOrdinal(int segmentIndex, PromptAlternativeSpec alternative) {
        if (segmentIndex < 0) {
            return 0;
        }

        var ordinal = 0;
        for (var index = 1; index < Math.Min(segmentIndex, alternative.Segments.Length); index++) {
            if (alternative.Segments[index] is PromptSlotSegmentSpec) {
                ordinal += 1;
            }
        }

        return ordinal;
    }

    private static PromptStyledText BuildActiveResolutionSectionText(
        ResolvedPrompt context,
        PromptSemanticAnalysis analysis,
        PromptInputState state,
        PromptSurfaceScenario scenario,
        IReadOnlyList<string> diagnostics,
        out IReadOnlyList<string> remainingDiagnostics) {
        remainingDiagnostics = diagnostics;
        if (!TryResolveActiveSlotStatus(context, analysis, state, scenario, out var status)) {
            return CreateStyledText(string.Empty);
        }

        var detailLine = BuildActiveSlotDetailLine(status, diagnostics, out remainingDiagnostics);
        if (string.IsNullOrWhiteSpace(detailLine)) {
            return CreateStyledText(string.Empty);
        }

        return CreateStyledText(detailLine);
    }

    private static PromptStyledText ComposeStyledText(string prefix, PromptStyledText content, string suffix = "") {
        string safePrefix = prefix ?? string.Empty;
        string safeContent = content?.Text ?? string.Empty;
        string safeSuffix = suffix ?? string.Empty;
        PromptHighlightSpan[] highlights = content?.Highlights is not { Length: > 0 } sourceHighlights
            ? []
            : [.. sourceHighlights.Select(highlight => new PromptHighlightSpan(
                highlight.StartIndex + safePrefix.Length,
                highlight.Length,
                highlight.StyleId))];
        return new PromptStyledText {
            Text = safePrefix + safeContent + safeSuffix,
            Highlights = highlights,
        };
    }

    private static PromptStyledText CreateStyledText(string? text) {
        return new PromptStyledText {
            Text = text?.Trim() ?? string.Empty,
        };
    }

    private static bool TryResolveActiveSlotStatus(
        ResolvedPrompt context,
        PromptSemanticAnalysis analysis,
        PromptInputState state,
        PromptSurfaceScenario scenario,
        out ActiveSlotStatus status) {
        status = default;
        if (!TryCreateExplainContext(context, analysis, state, scenario, out var explainContext)
            || string.IsNullOrWhiteSpace(explainContext.RawToken)
            || explainContext.ActiveSlot.SemanticKey is not SemanticKey semanticKey
            || explainContext.ActiveSlot.AcceptedSpecialTokens.Any(token => token.Equals(explainContext.RawToken, StringComparison.OrdinalIgnoreCase))) {
            return false;
        }

        var explainer = context.ResolveParameterExplainer(semanticKey);
        if (explainer is null) {
            return false;
        }

        try {
            if (!explainer.TryExplain(explainContext, out var result)) {
                return false;
            }

            status = new ActiveSlotStatus(explainContext, result);
            return true;
        }
        catch {
            return false;
        }
    }

    private static string BuildActiveSlotDetailLine(
        ActiveSlotStatus status,
        IReadOnlyList<string> diagnostics,
        out IReadOnlyList<string> remainingDiagnostics) {
        remainingDiagnostics = diagnostics;
        var displayText = ResolveActiveSlotDisplayText(status.ExplainResult);
        if (string.IsNullOrWhiteSpace(displayText)) {
            return string.Empty;
        }

        if (status.ExplainResult.State == PromptParamExplainState.Invalid) {
            var invalidDiagnostics = diagnostics
                .Where(IsActiveSlotInvalidDiagnostic)
                .ToArray();
            if (invalidDiagnostics.Length > 0) {
                remainingDiagnostics = diagnostics
                    .Where(diagnostic => !IsActiveSlotInvalidDiagnostic(diagnostic))
                    .ToArray();
                displayText = MergeInvalidStatusDisplayText(displayText, invalidDiagnostics[0]);
            }
        }

        return $"{status.ExplainContext.RawToken} -> {displayText}";
    }

    private static string ResolveActiveSlotDisplayText(PromptParamExplainResult result) {
        if (!string.IsNullOrWhiteSpace(result.DisplayText)) {
            return result.DisplayText.Trim();
        }

        return result.State switch {
            PromptParamExplainState.Ambiguous => "ambiguous",
            PromptParamExplainState.Invalid => "invalid",
            _ => string.Empty,
        };
    }

    private static bool IsActiveSlotInvalidDiagnostic(string diagnostic) {
        return !string.IsNullOrWhiteSpace(diagnostic)
            && diagnostic.TrimStart().StartsWith("invalid", StringComparison.OrdinalIgnoreCase);
    }

    private static string MergeInvalidStatusDisplayText(string displayText, string diagnostic) {
        var detailText = string.IsNullOrWhiteSpace(displayText)
            ? "invalid"
            : displayText.Trim();
        var diagnosticText = diagnostic?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(diagnosticText)
            || diagnosticText.Equals("invalid", StringComparison.OrdinalIgnoreCase)
            || diagnosticText.Equals(detailText, StringComparison.OrdinalIgnoreCase)) {
            return detailText;
        }

        if (detailText.StartsWith("invalid", StringComparison.OrdinalIgnoreCase)) {
            if (diagnosticText.StartsWith("invalid", StringComparison.OrdinalIgnoreCase)) {
                return diagnosticText.Length > detailText.Length ? diagnosticText : detailText;
            }

            return detailText + " - " + diagnosticText;
        }

        return diagnosticText.StartsWith("invalid", StringComparison.OrdinalIgnoreCase)
            ? diagnosticText
            : detailText + " - " + diagnosticText;
    }

    private readonly record struct ActiveSlotStatus(
        PromptParamExplainContext ExplainContext,
        PromptParamExplainResult ExplainResult);

    private static string ResolveSlotLabel(
        PromptSlotSegmentSpec slot,
        int fallbackIndex,
        bool preferArgFallback = true) {
        if (slot.SemanticKey is SemanticKey semanticKey
            && !string.IsNullOrWhiteSpace(semanticKey.DisplayName)) {
            return semanticKey.DisplayName.Trim();
        }

        var displayLabel = FormatParameterName(slot.DisplayLabel);
        if (!string.IsNullOrWhiteSpace(displayLabel)) {
            return displayLabel;
        }

        var slotName = FormatParameterName(slot.Name);
        if (!string.IsNullOrWhiteSpace(slotName)) {
            return slotName;
        }

        var completionKindId = slot.CompletionKindId?.Trim();
        if (string.Equals(completionKindId, PromptSuggestionKindIds.Boolean, StringComparison.Ordinal)) {
            return "bool";
        }

        if (string.Equals(completionKindId, PromptSuggestionKindIds.Enum, StringComparison.Ordinal)) {
            return "enum";
        }

        return preferArgFallback ? "arg" + (fallbackIndex + 1) : "plain";
    }

    private static string? FormatParameterName(string? name) {
        if (string.IsNullOrWhiteSpace(name)) {
            return null;
        }

        var value = name.Trim().Replace('-', ' ').Replace('_', ' ');
        StringBuilder builder = new(value.Length + 4);
        for (var index = 0; index < value.Length; index++) {
            var current = value[index];
            if (char.IsWhiteSpace(current)) {
                if (builder.Length > 0 && builder[^1] != ' ') {
                    builder.Append(' ');
                }

                continue;
            }

            if (builder.Length > 0 && builder[^1] != ' ') {
                var previous = value[index - 1];
                var splitCamelCase = char.IsUpper(current) && (
                    char.IsLower(previous)
                    || (char.IsUpper(previous)
                        && index + 1 < value.Length
                        && char.IsLower(value[index + 1])));
                var splitDigitBoundary = (char.IsDigit(current) && char.IsLetter(previous))
                    || (char.IsLetter(current) && char.IsDigit(previous));
                if (splitCamelCase || splitDigitBoundary) {
                    builder.Append(' ');
                }
            }

            builder.Append(char.ToLowerInvariant(current));
        }

        var normalized = builder.ToString().Trim();
        return normalized.Length == 0 ? null : normalized;
    }

    private static string ResolveModifierDisplayLabel(PromptModifierOptionSpec modifier) {
        var displayLabel = FormatParameterName(modifier.DisplayLabel);
        if (!string.IsNullOrWhiteSpace(displayLabel)) {
            return displayLabel;
        }

        var keyLabel = FormatParameterName(modifier.Key);
        if (!string.IsNullOrWhiteSpace(keyLabel)) {
            return keyLabel;
        }

        return modifier.CanonicalToken?.Trim() ?? string.Empty;
    }

    private readonly record struct DisplaySegmentDescriptor(int SegmentIndex, PromptSegmentSpec Segment);

    private readonly record struct ArgumentDisplayToken(string Text, bool IsVariadic, int SegmentIndex);
}
