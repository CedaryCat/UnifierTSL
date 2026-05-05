using System.Collections.Immutable;
using System.Globalization;
using UnifierTSL.Surface.Prompting;
using UnifierTSL.Commanding;
using UnifierTSL.Commanding.Bindings;
using UnifierTSL.Surface.Prompting.Model;

namespace UnifierTSL.Surface.Prompting.Semantics;

internal enum PromptFocusKind : byte
{
    None,
    Literal,
    Slot,
    Modifier,
    Overflow,
}

internal readonly record struct PromptExpectation(
    PromptFocusKind Kind,
    PromptSlotSegmentSpec? Slot,
    int SegmentIndex,
    ImmutableArray<string> ExpectedLiterals);

internal readonly record struct PromptSemanticAnalysis(
    PromptAlternativeSpec ActiveAlternative,
    ImmutableArray<PromptAlternativeSpec> CompatibleAlternatives,
    ImmutableArray<PromptAlternativeSpec> ActiveInterpretationAlternatives,
    PromptInterpretationState InterpretationState,
    PromptFocusKind FocusKind,
    PromptExpectation AcceptExpectation,
    PromptSlotSegmentSpec? ActiveSlot,
    int ActiveSegmentIndex,
    PromptEditTarget EditTarget,
    PromptEditTarget SlotInputTarget,
    ImmutableArray<string> ExpectedLiterals,
    ImmutableArray<string> AvailableLiterals,
    ImmutableArray<PromptModifierOptionSpec> AvailableModifiers,
    ImmutableArray<PromptModifierOptionSpec> AllModifiers,
    ImmutableArray<PromptModifierOptionSpec> RecognizedFlags,
    ImmutableArray<string> Diagnostics,
    ImmutableArray<PromptHighlightSpan> HighlightSpans);

internal static class PromptSemanticAnalyzer
{
    private static readonly ImmutableArray<string> BooleanRouteTokens = [
        "true", "false", "on", "off", "yes", "no", "y", "n", "1", "0",
    ];

    public static PromptSemanticAnalysis? TryAnalyze(
        ResolvedPrompt context,
        PromptInputState state,
        PromptSurfaceScenario scenario,
        string? input = null) {

        var spec = context.SemanticSpec;
        if (spec is null || spec.Alternatives.Length == 0) {
            return null;
        }

        string sourceText = input ?? state.InputText;
        var parse = PromptInputLexer.Parse(sourceText, spec.ActivationPrefixes);
        List<AlternativeEvaluation> evaluations = EvaluateAlternatives(
            context,
            state,
            scenario,
            parse,
            spec.Alternatives);

        if (evaluations.Count == 0) {
            return null;
        }

        var resolution = ResolveInterpretationResolution(evaluations, state.PreferredInterpretationId);
        var active = resolution.ActiveEvaluation;
        var activeInterpretation = resolution.ActiveInterpretationEvaluations.Length == 0
            ? [active]
            : resolution.ActiveInterpretationEvaluations;

        HashSet<(int Start, int Length, string StyleId)> seenHighlights = [];
        List<PromptHighlightSpan> highlights = [];
        foreach (var evaluation in activeInterpretation) {
            foreach (var highlight in evaluation.HighlightSpans) {
                if (seenHighlights.Add((highlight.StartIndex, highlight.Length, highlight.StyleId ?? string.Empty))) {
                    highlights.Add(highlight);
                }
            }
        }

        return new PromptSemanticAnalysis(
            ActiveAlternative: active.Alternative,
            CompatibleAlternatives: [.. resolution.RankedEvaluations.Select(static evaluation => evaluation.Alternative)],
            ActiveInterpretationAlternatives: [.. activeInterpretation.Select(static evaluation => evaluation.Alternative)],
            InterpretationState: resolution.InterpretationState,
            FocusKind: active.FocusKind,
            AcceptExpectation: active.AcceptExpectation,
            ActiveSlot: active.ActiveSlot,
            ActiveSegmentIndex: active.ActiveSegmentIndex,
            EditTarget: active.EditTarget,
            SlotInputTarget: active.SlotInputTarget,
            ExpectedLiterals: active.ExpectedLiterals,
            AvailableLiterals: ResolveAvailableLiterals(active, resolution.RankedEvaluations),
            AvailableModifiers: [.. activeInterpretation
                .SelectMany(static evaluation => evaluation.AvailableModifiers)
                .GroupBy(static modifier => modifier.CanonicalToken, StringComparer.OrdinalIgnoreCase)
                .Select(static group => group.First())],
            AllModifiers: [.. active.AllModifiers
                .GroupBy(static modifier => modifier.CanonicalToken, StringComparer.OrdinalIgnoreCase)
                .Select(static group => group.First())],
            RecognizedFlags: active.RecognizedFlags,
            Diagnostics: active.Diagnostics,
            HighlightSpans: [.. highlights
                .OrderBy(static highlight => highlight.StartIndex)
                .ThenBy(static highlight => highlight.Length)]);
    }

    private static ImmutableArray<string> ResolveAvailableLiterals(
        AlternativeEvaluation activeEvaluation,
        ImmutableArray<AlternativeEvaluation> compatibleEvaluations) {
        return [.. compatibleEvaluations
            .Where(evaluation =>
                evaluation.FocusKind == PromptFocusKind.Literal
                && evaluation.ActiveSegmentIndex == activeEvaluation.ActiveSegmentIndex
                && evaluation.EditTarget.StartIndex == activeEvaluation.EditTarget.StartIndex
                && evaluation.EditTarget.Length == activeEvaluation.EditTarget.Length
                && string.Equals(evaluation.EditTarget.RawText, activeEvaluation.EditTarget.RawText, StringComparison.Ordinal))
            .SelectMany(static evaluation => evaluation.ExpectedLiterals)
            .Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    private static List<AlternativeEvaluation> EvaluateAlternatives(
        ResolvedPrompt context,
        PromptInputState state,
        PromptSurfaceScenario scenario,
        PromptInputParseResult parse,
        IReadOnlyList<PromptAlternativeSpec> alternatives) {
        List<AlternativeEvaluation> evaluations = [];
        for (var alternativeIndex = 0; alternativeIndex < alternatives.Count; alternativeIndex++) {
            PromptAlternativeSpec alternative = alternatives[alternativeIndex];
            AlternativeEvaluation evaluation = EvaluateAlternative(
                context,
                state,
                scenario,
                parse,
                alternative,
                alternativeIndex);
            if (evaluation.Compatible) {
                evaluations.Add(evaluation);
            }
        }

        return evaluations;
    }

    private static AlternativeEvaluation EvaluateAlternative(
        ResolvedPrompt context,
        PromptInputState state,
        PromptSurfaceScenario scenario,
        PromptInputParseResult parse,
        PromptAlternativeSpec alternative,
        int alternativeIndex) {
        Dictionary<string, PromptSlotSegmentSpec> slotsByName = alternative.Segments
            .OfType<PromptSlotSegmentSpec>()
            .Where(static slot => !string.IsNullOrWhiteSpace(slot.Name))
            .ToDictionary(static slot => slot.Name, static slot => slot, StringComparer.OrdinalIgnoreCase);
        List<AlternativeEvaluation> candidates = [];

        foreach (var inputView in ResolveInputViews(parse)) {
            List<CompletedMatchState> matches = [];
            CollectCompletedMatches(
                context,
                state,
                scenario,
                parse,
                alternative,
                inputView.CommittedTokenCount,
                tokenIndex: 0,
                segmentIndex: 0,
                slotsByName,
                new CompletedMatchState(),
                matches);

            foreach (var match in matches) {
                var evaluation = TryBuildEvaluationFromMatch(
                    match,
                    context,
                    state,
                    scenario,
                    parse,
                    inputView,
                    alternative,
                    alternativeIndex,
                    slotsByName);
                if (evaluation is { } compatible) {
                    candidates.Add(compatible);
                }
            }
        }

        return OrderEvaluations(candidates).FirstOrDefault(AlternativeEvaluation.Incompatible(alternative, alternativeIndex));
    }

    private static IEnumerable<PromptInputView> ResolveInputViews(PromptInputParseResult parse) {
        // The lexer reports only lexical facts: token spans plus whether the raw input currently
        // ends at a separator. Semantic analysis then derives one or more views from that tail.
        //
        // For trailing separators we intentionally consider both interpretations up front:
        // 1. the token before the separator is already committed, and
        // 2. that token is still the live edit target the operator is correcting.
        //
        // Ranking between those views inside the analyzer keeps "space commits a token" aligned
        // with the active slot's consumption semantics instead of baking that decision into the
        // lexer or re-running a fallback parse after every failure.
        yield return CreateCommittedInputView(parse);
        if (parse.HasTrailingSeparator && parse.Tokens.Length > 0) {
            PromptInputToken liveTailToken = parse.Tokens[^1];
            yield return new PromptInputView(
                CommittedTokenCount: parse.Tokens.Length - 1,
                LiveToken: liveTailToken,
                LiveText: liveTailToken.Value,
                LiveTextStart: liveTailToken.StartIndex,
                LiveTextQuoted: liveTailToken.Quoted,
                LiveTextLeadingCharacterEscaped: liveTailToken.LeadingCharacterEscaped,
                HasTrailingSeparator: true);
        }
    }

    private static PromptInputView CreateCommittedInputView(PromptInputParseResult parse) {
        PromptInputToken? liveToken = !parse.HasTrailingSeparator && parse.Tokens.Length > 0
            ? parse.Tokens[^1]
            : null;
        return new PromptInputView(
            CommittedTokenCount: parse.HasTrailingSeparator
                ? parse.Tokens.Length
                : Math.Max(0, parse.Tokens.Length - 1),
            LiveToken: liveToken,
            LiveText: liveToken?.Value ?? parse.TailText,
            LiveTextStart: liveToken?.StartIndex ?? parse.TailTextStart,
            LiveTextQuoted: liveToken?.Quoted ?? parse.TailTextQuoted,
            LiveTextLeadingCharacterEscaped: liveToken?.LeadingCharacterEscaped ?? parse.TailTextLeadingCharacterEscaped,
            HasTrailingSeparator: parse.HasTrailingSeparator);
    }

    private static void CollectCompletedMatches(
        ResolvedPrompt context,
        PromptInputState state,
        PromptSurfaceScenario scenario,
        PromptInputParseResult parse,
        PromptAlternativeSpec alternative,
        int completedCount,
        int tokenIndex,
        int segmentIndex,
        IReadOnlyDictionary<string, PromptSlotSegmentSpec> slotsByName,
        CompletedMatchState working,
        List<CompletedMatchState> matches) {

        if (tokenIndex >= completedCount) {
            working.SegmentIndex = SkipSatisfiedSegments(
                alternative.Segments,
                segmentIndex,
                nextToken: null,
                working.NamedAssignments);
            matches.Add(working);
            return;
        }

        var token = parse.Tokens[tokenIndex];
        if (working.PendingNamedOption is PromptNamedOptionSpec pendingNamedOption) {
            if (!slotsByName.TryGetValue(pendingNamedOption.SlotName, out var pendingSlot)) {
                return;
            }

            var namedValueMatch = EvaluateRouteMatch(
                context,
                state,
                scenario,
                alternative,
                pendingSlot,
                [token],
                isPartial: false);
            if (!namedValueMatch.Compatible) {
                return;
            }

            var next = working.Clone();
            next.PendingNamedOption = null;
            next.TailSlotSegmentIndex = -1;
            next.NamedAssignments[pendingSlot.Name] = [token];
            next.CompletedPathSpecificity += namedValueMatch.Specificity;
            if (token.SourceLength > 0) {
                next.Highlights.Add(new PromptHighlightSpan(token.StartIndex, token.SourceLength, PromptStyleKeys.SyntaxValue));
            }

            CollectCompletedMatches(
                context,
                state,
                scenario,
                parse,
                alternative,
                completedCount,
                tokenIndex + 1,
                segmentIndex,
                slotsByName,
                next,
                matches);
            return;
        }

        var effectiveSegmentIndex = SkipSatisfiedSegments(
            alternative.Segments,
            segmentIndex,
            token,
            working.NamedAssignments);
        if (TryConsumeModifierToken(alternative, effectiveSegmentIndex, token, out var modifierIdentity)) {
            var next = working.Clone();
            next.TailSlotSegmentIndex = -1;
            next.RecognizedModifierKeys.Add(modifierIdentity!);
            next.Highlights.Add(new PromptHighlightSpan(token.StartIndex, token.SourceLength, PromptStyleKeys.SyntaxModifier));
            CollectCompletedMatches(
                context,
                state,
                scenario,
                parse,
                alternative,
                completedCount,
                tokenIndex + 1,
                segmentIndex,
                slotsByName,
                next,
                matches);
        }

        if (TryConsumeNamedOption(alternative, effectiveSegmentIndex, token, out var namedOption)) {
            var next = working.Clone();
            next.PendingNamedOption = namedOption;
            next.TailSlotSegmentIndex = -1;
            next.Highlights.Add(new PromptHighlightSpan(token.StartIndex, token.SourceLength, PromptStyleKeys.SyntaxModifier));
            CollectCompletedMatches(
                context,
                state,
                scenario,
                parse,
                alternative,
                completedCount,
                tokenIndex + 1,
                segmentIndex,
                slotsByName,
                next,
                matches);
        }

        if (ShouldExcludeRecognizedTokenFromSlotFlow(alternative, effectiveSegmentIndex, token)) {
            return;
        }

        effectiveSegmentIndex = SkipSatisfiedSegments(
            alternative.Segments,
            segmentIndex,
            token,
            working.NamedAssignments);
        if (effectiveSegmentIndex >= alternative.Segments.Length) {
            if (alternative.OverflowBehavior == PromptOverflowBehavior.IgnoreAdditionalTokens) {
                var next = working.Clone();
                next.TailSlotSegmentIndex = -1;
                next.SegmentIndex = alternative.Segments.Length;
                matches.Add(next);
            }

            return;
        }

        var segment = alternative.Segments[effectiveSegmentIndex];
        if (segment is PromptLiteralSegmentSpec literalSegment) {
            if (!token.Value.Equals(literalSegment.Value, StringComparison.OrdinalIgnoreCase)) {
                return;
            }

            var next = working.Clone();
            next.LiteralMatches += 1;
            next.ConsumedOrderedTokens += 1;
            next.CompletedPathSpecificity += 1000 + literalSegment.Value.Length;
            next.TailSlotSegmentIndex = -1;
            next.Highlights.Add(new PromptHighlightSpan(token.StartIndex, token.SourceLength, literalSegment.HighlightStyleId));
            CollectCompletedMatches(
                context,
                state,
                scenario,
                parse,
                alternative,
                completedCount,
                tokenIndex + 1,
                effectiveSegmentIndex + 1,
                slotsByName,
                next,
                matches);
            return;
        }

        PromptSlotSegmentSpec slotSegment = (PromptSlotSegmentSpec)segment;
        var remainingTokenCount = completedCount - tokenIndex;
        foreach (var spanLength in ResolveCompletedSpanLengths(slotSegment, remainingTokenCount)) {
            PromptInputToken[] spanTokens = [.. parse.Tokens.Skip(tokenIndex).Take(spanLength)];
            var routeMatch = EvaluateRouteMatch(
                context,
                state,
                scenario,
                alternative,
                slotSegment,
                spanTokens,
                isPartial: false);
            if (!routeMatch.Compatible) {
                continue;
            }

            var next = working.Clone();
            if (!next.SlotTokens.TryGetValue(effectiveSegmentIndex, out var capturedTokens)) {
                capturedTokens = [];
                next.SlotTokens[effectiveSegmentIndex] = capturedTokens;
            }

            capturedTokens.AddRange(spanTokens);
            next.ConsumedOrderedTokens += spanLength;
            next.CompletedPathSpecificity += spanLength * 100 + routeMatch.Specificity;
            next.TailSlotSegmentIndex = tokenIndex + spanLength == completedCount && CanTailContinue(slotSegment)
                ? effectiveSegmentIndex
                : -1;
            foreach (var spanToken in spanTokens) {
                if (spanToken.SourceLength > 0) {
                    next.Highlights.Add(new PromptHighlightSpan(spanToken.StartIndex, spanToken.SourceLength, PromptStyleKeys.SyntaxValue));
                }
            }

            CollectCompletedMatches(
                context,
                state,
                scenario,
                parse,
                alternative,
                completedCount,
                tokenIndex + spanLength,
                effectiveSegmentIndex + 1,
                slotsByName,
                next,
                matches);
        }
    }

    private static AlternativeEvaluation? TryBuildEvaluationFromMatch(
        CompletedMatchState match,
        ResolvedPrompt context,
        PromptInputState state,
        PromptSurfaceScenario scenario,
        PromptInputParseResult parse,
        PromptInputView inputView,
        PromptAlternativeSpec alternative,
        int alternativeIndex,
        IReadOnlyDictionary<string, PromptSlotSegmentSpec> slotsByName) {

        PromptInputToken? currentToken = inputView.LiveToken;
        List<PromptHighlightSpan> highlights = [.. match.Highlights];
        List<string> diagnostics = [];

        var availableModifiers = ResolveAvailableModifiers(
            alternative,
            match.SegmentIndex,
            match.RecognizedModifierKeys,
            currentToken);

        var exactModifierMatch = ResolveExactModifierMatch(
            alternative,
            match.SegmentIndex,
            currentToken);

        var exactNamedOption = ResolveExactNamedOption(
            alternative,
            match.SegmentIndex,
            currentToken);

        PromptFocusKind focusKind;
        PromptSlotSegmentSpec? activeSlot = null;
        var activeSegmentIndex = -1;
        ImmutableArray<string> expectedLiterals = [];
        var routeCompatible = true;
        var currentSlotSpecificity = 0;
        var currentPathSpecificity = match.CompletedPathSpecificity;
        var preferTrailingSeparatorCommit = false;

        if (match.PendingNamedOption is PromptNamedOptionSpec pendingNamedOption
            && slotsByName.TryGetValue(pendingNamedOption.SlotName, out var pendingSlot)) {
            focusKind = PromptFocusKind.Slot;
            activeSlot = pendingSlot;
            activeSegmentIndex = ResolveSegmentIndex(alternative, pendingSlot);
            if (currentToken is PromptInputToken pendingValueToken) {
                var pendingRouteMatch = EvaluateRouteMatch(
                    context,
                    state,
                    scenario,
                    alternative,
                    pendingSlot,
                    [pendingValueToken],
                    isPartial: true);
                routeCompatible = pendingRouteMatch.Compatible;
                currentSlotSpecificity = pendingRouteMatch.Specificity;
                currentPathSpecificity += pendingRouteMatch.Specificity;
                diagnostics.AddRange(pendingRouteMatch.Diagnostics);
                if (pendingValueToken.SourceLength > 0) {
                    highlights.Add(new PromptHighlightSpan(
                        pendingValueToken.StartIndex,
                        pendingValueToken.SourceLength,
                        ResolveRouteStyleId(pendingRouteMatch.Compatible)));
                }
            }
        }
        else if (currentToken is PromptInputToken modifierToken
            && IsModifierPrefixToken(modifierToken, availableModifiers)) {
            focusKind = PromptFocusKind.Modifier;
            activeSegmentIndex = match.SegmentIndex;
            currentPathSpecificity += availableModifiers
                .SelectMany(static option => option.Tokens)
                .Where(candidate => candidate.StartsWith(modifierToken.Value, StringComparison.OrdinalIgnoreCase))
                .Select(candidate => ResolvePrefixSpecificityScore(candidate, modifierToken.Value))
                .DefaultIfEmpty(0)
                .Max();
        }
        else if (currentToken is PromptInputToken activeToken
            && TryResolveTailSlotContinuation(
                context,
                state,
                scenario,
                alternative,
                match,
                activeToken,
                out var tailSlot,
                out var tailSegmentIndex,
                out var tailRouteMatch)) {
            // Greedy/remaining routes must get first claim on the live token or they collapse
            // back into "single token then next arg" behavior and branch far too early.
            focusKind = PromptFocusKind.Slot;
            activeSlot = tailSlot;
            activeSegmentIndex = tailSegmentIndex;
            routeCompatible = tailRouteMatch.Compatible;
            currentSlotSpecificity = tailRouteMatch.Specificity;
            currentPathSpecificity += tailRouteMatch.Specificity;
            diagnostics.AddRange(tailRouteMatch.Diagnostics);
            if (activeToken.SourceLength > 0) {
                highlights.Add(new PromptHighlightSpan(
                    activeToken.StartIndex,
                    activeToken.SourceLength,
                    ResolveRouteStyleId(tailRouteMatch.Compatible)));
            }
        }
        else {
            var effectiveSegmentIndex = SkipSatisfiedSegments(
                alternative.Segments,
                match.SegmentIndex,
                currentToken,
                match.NamedAssignments);
            if (inputView.HasTrailingSeparator
                && currentToken is null
                && match.TailSlotSegmentIndex >= 0
                && ResolveSlot(alternative, match.TailSlotSegmentIndex) is PromptSlotSegmentSpec trailingSlot
                && ShouldStayOnTailSlotAfterSpace(trailingSlot)) {
                focusKind = PromptFocusKind.Slot;
                activeSlot = trailingSlot;
                activeSegmentIndex = match.TailSlotSegmentIndex;
                routeCompatible = true;
                currentSlotSpecificity = ResolveDefaultRouteSpecificity(trailingSlot);
                currentPathSpecificity += currentSlotSpecificity;
                preferTrailingSeparatorCommit = true;
            }
            else if (effectiveSegmentIndex >= alternative.Segments.Length) {
                focusKind = currentToken is null || alternative.OverflowBehavior == PromptOverflowBehavior.IgnoreAdditionalTokens
                    ? PromptFocusKind.None
                    : PromptFocusKind.Overflow;
            }
            else if (alternative.Segments[effectiveSegmentIndex] is PromptLiteralSegmentSpec activeLiteral) {
                if (currentToken is PromptInputToken partialLiteral
                    && partialLiteral.Value.Length > 0
                    && !activeLiteral.Value.StartsWith(partialLiteral.Value, StringComparison.OrdinalIgnoreCase)) {
                    return null;
                }

                focusKind = PromptFocusKind.Literal;
                activeSegmentIndex = effectiveSegmentIndex;
                expectedLiterals = [activeLiteral.Value];
                if (currentToken is PromptInputToken literalToken && literalToken.SourceLength > 0) {
                    highlights.Add(new PromptHighlightSpan(
                        literalToken.StartIndex,
                        literalToken.SourceLength,
                        activeLiteral.HighlightStyleId));
                    currentPathSpecificity += ResolvePrefixSpecificityScore(activeLiteral.Value, literalToken.Value);
                }
            }
            else {
                focusKind = PromptFocusKind.Slot;
                activeSlot = (PromptSlotSegmentSpec)alternative.Segments[effectiveSegmentIndex];
                activeSegmentIndex = effectiveSegmentIndex;
                if (currentToken is PromptInputToken slotToken) {
                    var routeMatch = EvaluateRouteMatch(
                        context,
                        state,
                        scenario,
                        alternative,
                        activeSlot,
                        [slotToken],
                        isPartial: true);
                    routeCompatible = routeMatch.Compatible;
                    currentSlotSpecificity = routeMatch.Specificity;
                    currentPathSpecificity += routeMatch.Specificity;
                    diagnostics.AddRange(routeMatch.Diagnostics);
                    if (slotToken.SourceLength > 0) {
                        highlights.Add(new PromptHighlightSpan(
                            slotToken.StartIndex,
                            slotToken.SourceLength,
                            ResolveRouteStyleId(routeMatch.Compatible)));
                    }
                }
                else {
                    currentSlotSpecificity = ResolveDefaultRouteSpecificity(activeSlot);
                    currentPathSpecificity += currentSlotSpecificity;
                }
            }
        }

        if (currentToken is PromptInputToken decoratedModifierToken
            && decoratedModifierToken.SourceLength > 0
            && (focusKind == PromptFocusKind.Modifier
                || exactModifierMatch is not null
                || exactNamedOption is not null)) {
            highlights.Add(new PromptHighlightSpan(
                decoratedModifierToken.StartIndex,
                decoratedModifierToken.SourceLength,
                PromptStyleKeys.SyntaxModifier));
        }

        var editTarget = BuildEditTarget(
            parse,
            inputView,
            activeSlot,
            match.SlotTokens,
            activeSegmentIndex);
        var slotInputTarget = BuildSlotInputTarget(
            parse,
            inputView,
            match.SlotTokens,
            activeSegmentIndex,
            editTarget);
        var acceptExpectation = ResolveAcceptExpectation(
            alternative,
            activeSegmentIndex >= 0 ? activeSegmentIndex : match.SegmentIndex,
            currentToken,
            match.NamedAssignments,
            slotsByName,
            match.RecognizedModifierKeys,
            availableModifiers,
            match.PendingNamedOption,
            exactModifierMatch,
            exactNamedOption);
        var guardBucket = ResolveGuardBucket(context, alternative, parse);

        return new AlternativeEvaluation(
            Alternative: alternative,
            AlternativeIndex: alternativeIndex,
            Compatible: true,
            RouteCompatible: routeCompatible,
            GuardState: guardBucket.State,
            GuardBucketKey: guardBucket.Key,
            GuardBucketLabel: guardBucket.Label,
            RouteSignatureKey: activeSlot is null || focusKind != PromptFocusKind.Slot
                ? string.Empty
                : ResolveRouteSignatureKey(activeSlot),
            RouteSignatureLabel: activeSlot is null || focusKind != PromptFocusKind.Slot
                ? string.Empty
                : ResolveRouteSignatureLabel(activeSlot),
            FocusKind: focusKind,
            AcceptExpectation: acceptExpectation,
            ActiveSlot: activeSlot,
            ActiveSegmentIndex: activeSegmentIndex,
            EditTarget: editTarget,
            SlotInputTarget: slotInputTarget,
            ExpectedLiterals: expectedLiterals,
            AvailableModifiers: [.. availableModifiers],
            AllModifiers: [.. ResolveAllModifiers(alternative)],
            RecognizedFlags: [.. ResolveRecognizedFlags(alternative, match.RecognizedModifierKeys)],
            HighlightSpans: [.. highlights],
            Diagnostics: [.. diagnostics],
            PathSpecificity: currentPathSpecificity,
            CurrentSlotSpecificity: currentSlotSpecificity,
            PreferTrailingSeparatorCommit: preferTrailingSeparatorCommit,
            RemainingRequiredShapes: CountRemainingRequiredSegments(
                alternative.Segments,
                ResolveRemainingShapeStartIndex(focusKind, activeSegmentIndex, match.SegmentIndex),
                match.NamedAssignments));
    }

    private static InterpretationResolution ResolveInterpretationResolution(List<AlternativeEvaluation> evaluations, string? preferredInterpretationId) {
        ImmutableArray<AlternativeEvaluation> rankedEvaluations = [.. OrderEvaluations(evaluations)];
        var freshInputCluster = ResolveFreshInputContinuationCluster(rankedEvaluations);
        if (freshInputCluster.Length > 0) {
            return ResolveContinuationResolution(rankedEvaluations, freshInputCluster, preferredInterpretationId);
        }

        var fallbackActive = rankedEvaluations[0];
        if (fallbackActive.FocusKind != PromptFocusKind.Slot || !fallbackActive.RouteCompatible) {
            return new InterpretationResolution(
                RankedEvaluations: rankedEvaluations,
                ActiveEvaluation: fallbackActive,
                ActiveInterpretationEvaluations: [fallbackActive],
                InterpretationState: CreateInterpretationState(fallbackActive));
        }

        return ResolveSlotBranchResolution(
            rankedEvaluations,
            rankedEvaluations,
            fallbackActive,
            preferredInterpretationId,
            fallbackInterpretationEvaluations: [fallbackActive]);
    }

    private static InterpretationResolution ResolveContinuationResolution(
        ImmutableArray<AlternativeEvaluation> rankedEvaluations,
        ImmutableArray<AlternativeEvaluation> continuationCluster,
        string? preferredInterpretationId) {

        var activeEvaluation = continuationCluster[0];
        if (activeEvaluation.FocusKind != PromptFocusKind.Slot || !activeEvaluation.RouteCompatible) {
            return new InterpretationResolution(
                RankedEvaluations: rankedEvaluations,
                ActiveEvaluation: activeEvaluation,
                ActiveInterpretationEvaluations: continuationCluster,
                InterpretationState: CreateInterpretationState(activeEvaluation));
        }

        return ResolveSlotBranchResolution(
            rankedEvaluations,
            continuationCluster,
            activeEvaluation,
            preferredInterpretationId,
            fallbackInterpretationEvaluations: continuationCluster);
    }

    private static InterpretationResolution ResolveSlotBranchResolution(
        ImmutableArray<AlternativeEvaluation> rankedEvaluations,
        ImmutableArray<AlternativeEvaluation> sourceEvaluations,
        AlternativeEvaluation fallbackActive,
        string? preferredInterpretationId,
        ImmutableArray<AlternativeEvaluation> fallbackInterpretationEvaluations) {

        List<InterpretationCandidate> interpretations = sourceEvaluations
            .Where(static evaluation => CanPublishInterpretation(evaluation))
            .GroupBy(static evaluation => evaluation.InterpretationId, StringComparer.Ordinal)
            .Select(static group => new InterpretationCandidate(group.Key, [.. group]))
            .OrderByDescending(static interpretation => interpretation.BestEvaluation.GuardState != PromptRouteGuardState.Deny)
            .ThenByDescending(static interpretation => interpretation.BestEvaluation.RouteCompatible)
            .ThenByDescending(static interpretation => interpretation.BestEvaluation.PreferTrailingSeparatorCommit)
            .ThenByDescending(static interpretation => interpretation.BestEvaluation.PathSpecificity)
            .ThenByDescending(static interpretation => interpretation.BestEvaluation.CurrentSlotSpecificity)
            .ThenBy(static interpretation => interpretation.BestEvaluation.RemainingRequiredShapes)
            .ThenBy(static interpretation => interpretation.BestEvaluation.AlternativeIndex)
            .ToList();
        interpretations = CollapseShadowedFreeTextInterpretations(interpretations);
        if (interpretations.Count == 0) {
            return new InterpretationResolution(
                RankedEvaluations: rankedEvaluations,
                ActiveEvaluation: fallbackActive,
                ActiveInterpretationEvaluations: fallbackInterpretationEvaluations,
                InterpretationState: CreateInterpretationState(fallbackActive));
        }

        var activeInterpretation = interpretations
            .FirstOrDefault(interpretation => interpretation.Id.Equals(preferredInterpretationId ?? string.Empty, StringComparison.Ordinal));
        if (activeInterpretation.Evaluations.IsDefaultOrEmpty) {
            activeInterpretation = interpretations[0];
        }
        var activeEvaluation = activeInterpretation.BestEvaluation;
        if (interpretations.Count == 1) {
            return new InterpretationResolution(
                RankedEvaluations: rankedEvaluations,
                ActiveEvaluation: activeEvaluation,
                ActiveInterpretationEvaluations: activeInterpretation.Evaluations,
                InterpretationState: CreateInterpretationState(activeEvaluation));
        }

        Dictionary<string, int> labelCounts = interpretations
            .Select(static interpretation => ResolveInterpretationBaseLabel(interpretation.BestEvaluation))
            .GroupBy(static label => label, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.OrdinalIgnoreCase);
        PromptInterpretation[] entries = [.. interpretations.Select(interpretation => new PromptInterpretation {
            Id = interpretation.Id,
            Label = ResolveInterpretationLabel(interpretation.BestEvaluation, labelCounts),
        })];

        return new InterpretationResolution(
            RankedEvaluations: rankedEvaluations,
            ActiveEvaluation: activeEvaluation,
            ActiveInterpretationEvaluations: activeInterpretation.Evaluations,
            InterpretationState: new PromptInterpretationState {
                ActiveInterpretationId = activeInterpretation.Id,
                ActiveInterpretationIndex = interpretations.FindIndex(interpretation => interpretation.Id.Equals(activeInterpretation.Id, StringComparison.Ordinal)),
                Interpretations = entries,
            });
    }

    private static ImmutableArray<AlternativeEvaluation> ResolveFreshInputContinuationCluster(
        ImmutableArray<AlternativeEvaluation> rankedEvaluations) {
        // A trailing separator has already committed the previous token. Once we are editing a
        // fresh slot/literal position, terminal interpretations like bare `ban` must step aside so the
        // prompt can surface real continuation candidates instead of "no more args".
        for (var index = 0; index < rankedEvaluations.Length; index++) {
            var seed = rankedEvaluations[index];
            if (!IsFreshInputContinuation(seed)) {
                continue;
            }

            return [.. rankedEvaluations.Where(candidate => BelongsToFreshInputCluster(candidate, seed))];
        }

        return [];
    }

    private static bool IsFreshInputContinuation(AlternativeEvaluation evaluation) {
        return evaluation.EditTarget.Length == 0
            && evaluation.EditTarget.RawText.Length == 0
            && evaluation.FocusKind is PromptFocusKind.Literal or PromptFocusKind.Slot or PromptFocusKind.Modifier
            && evaluation.GuardState != PromptRouteGuardState.Deny
            && (evaluation.FocusKind != PromptFocusKind.Slot || evaluation.RouteCompatible);
    }

    private static bool BelongsToFreshInputCluster(AlternativeEvaluation evaluation, AlternativeEvaluation seed) {
        return IsFreshInputContinuation(evaluation)
            && evaluation.FocusKind == seed.FocusKind
            && evaluation.ActiveSegmentIndex == seed.ActiveSegmentIndex
            && evaluation.EditTarget.StartIndex == seed.EditTarget.StartIndex;
    }

    private static IEnumerable<AlternativeEvaluation> OrderEvaluations(IEnumerable<AlternativeEvaluation> evaluations) {
        // A trailing separator on a multi-token tail slot means the operator has already opened
        // the next logical continuation position. That commit intent must outrank a more
        // specific "still editing the previous token" score or recursive RemainingText /
        // GreedyPhrase slots regress from `... Melvin ` back to `... Melvin`.
        return evaluations
            .OrderByDescending(static evaluation => evaluation.GuardState != PromptRouteGuardState.Deny)
            .ThenByDescending(static evaluation => evaluation.RouteCompatible)
            .ThenByDescending(static evaluation => evaluation.PreferTrailingSeparatorCommit)
            .ThenByDescending(static evaluation => evaluation.PathSpecificity)
            .ThenByDescending(static evaluation => evaluation.CurrentSlotSpecificity)
            .ThenBy(static evaluation => evaluation.RemainingRequiredShapes)
            .ThenBy(static evaluation => evaluation.AlternativeIndex);
    }

    private static bool CanPublishInterpretation(AlternativeEvaluation evaluation) {
        return evaluation.FocusKind == PromptFocusKind.Slot
            && evaluation.RouteCompatible
            && evaluation.GuardState != PromptRouteGuardState.Deny;
    }

    private static string ResolveInterpretationBaseLabel(AlternativeEvaluation evaluation) {
        if (!string.IsNullOrWhiteSpace(evaluation.RouteSignatureLabel)) {
            return evaluation.RouteSignatureLabel;
        }

        if (!string.IsNullOrWhiteSpace(evaluation.GuardBucketLabel)) {
            return evaluation.GuardBucketLabel;
        }

        return evaluation.Alternative.Title;
    }

    private static string ResolveInterpretationLabel(
        AlternativeEvaluation evaluation,
        IReadOnlyDictionary<string, int> labelCounts) {

        var baseLabel = ResolveInterpretationBaseLabel(evaluation);
        if (baseLabel.Length == 0) {
            return string.IsNullOrWhiteSpace(evaluation.Alternative.Title)
                ? evaluation.InterpretationId
                : evaluation.Alternative.Title;
        }

        if (labelCounts.TryGetValue(baseLabel, out var count)
            && count > 1
            && !string.IsNullOrWhiteSpace(evaluation.GuardBucketLabel)
            && !baseLabel.Equals(evaluation.GuardBucketLabel, StringComparison.OrdinalIgnoreCase)) {
            return $"{baseLabel} | {evaluation.GuardBucketLabel}";
        }

        return baseLabel;
    }

    private static PromptInterpretationState CreateInterpretationState(AlternativeEvaluation activeEvaluation) {
        return new PromptInterpretationState {
            ActiveInterpretationId = activeEvaluation.InterpretationId,
            ActiveInterpretationIndex = 0,
            Interpretations = [
                new PromptInterpretation {
                    Id = activeEvaluation.InterpretationId,
                    Label = ResolveInterpretationLabel(activeEvaluation, ImmutableDictionary<string, int>.Empty),
                },
            ],
        };
    }

    private static List<InterpretationCandidate> CollapseShadowedFreeTextInterpretations(List<InterpretationCandidate> interpretations) {
        if (interpretations.Count <= 1 || !HasConcreteTypedRouteMatch(interpretations[0].BestEvaluation)) {
            return interpretations;
        }

        // Once the live token concretely satisfies a typed route, keep generic free-text
        // fallbacks out of interpretation state. They are still parse-compatible, but no longer
        // represent a real prompt-time choice for cases like `help 1`.
        var dominant = interpretations[0].BestEvaluation;
        List<InterpretationCandidate> survivingInterpretations = [interpretations[0]];
        for (var index = 1; index < interpretations.Count; index++) {
            if (!IsShadowedFreeTextBranch(dominant, interpretations[index].BestEvaluation)) {
                survivingInterpretations.Add(interpretations[index]);
            }
        }

        return survivingInterpretations;
    }

    private static bool HasConcreteTypedRouteMatch(AlternativeEvaluation evaluation) {
        if (evaluation.FocusKind != PromptFocusKind.Slot
            || !evaluation.RouteCompatible
            || evaluation.ActiveSlot is not PromptSlotSegmentSpec slot
            || IsFreeTextRoute(slot)) {
            return false;
        }

        var rawText = evaluation.EditTarget.RawText;
        if (rawText.Length == 0) {
            return false;
        }

        var acceptedTokens = ResolveAcceptedTokenRoute(slot);
        if (acceptedTokens.Length > 0) {
            return acceptedTokens.Any(candidate => candidate.Equals(rawText, StringComparison.OrdinalIgnoreCase));
        }

        if (IsBooleanRoute(slot)) {
            return BooleanRouteTokens.Any(candidate => candidate.Equals(rawText, StringComparison.OrdinalIgnoreCase));
        }

        if (IsTimeOnlyRoute(slot)) {
            return TryParseWorldTime(rawText, out _);
        }

        if (IsIntegerRoute(slot)) {
            return int.TryParse(rawText, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
        }

        var defaultSpecificity = ResolveDefaultRouteSpecificity(slot);
        if (CommandPromptRoutes.IsSemanticLookupExact(slot.RouteMatchKind, slot.RouteSpecificity)) {
            return evaluation.CurrentSlotSpecificity >= defaultSpecificity + 300 + rawText.Length;
        }

        if (CommandPromptRoutes.IsSemanticLookupSoft(slot.RouteMatchKind, slot.RouteSpecificity)) {
            return evaluation.CurrentSlotSpecificity >= defaultSpecificity + 240 + rawText.Length;
        }

        return false;
    }

    private static bool IsShadowedFreeTextBranch(AlternativeEvaluation dominant, AlternativeEvaluation candidate) {
        return candidate.FocusKind == PromptFocusKind.Slot
            && candidate.RouteCompatible
            && candidate.ActiveSlot is PromptSlotSegmentSpec candidateSlot
            && IsFreeTextRoute(candidateSlot)
            && candidate.ActiveSegmentIndex == dominant.ActiveSegmentIndex
            && candidate.CurrentSlotSpecificity < dominant.CurrentSlotSpecificity
            && candidate.GuardBucketKey.Equals(dominant.GuardBucketKey, StringComparison.Ordinal);
    }

    private static bool TryResolveTailSlotContinuation(
        ResolvedPrompt context,
        PromptInputState state,
        PromptSurfaceScenario scenario,
        PromptAlternativeSpec alternative,
        CompletedMatchState match,
        PromptInputToken currentToken,
        out PromptSlotSegmentSpec? slot,
        out int segmentIndex,
        out RouteTokenMatch routeMatch) {

        slot = null;
        segmentIndex = -1;
        routeMatch = default;
        if (match.TailSlotSegmentIndex < 0
            || ResolveSlot(alternative, match.TailSlotSegmentIndex) is not PromptSlotSegmentSpec tailSlot
            || !CanTailContinue(tailSlot)) {
            return false;
        }

        List<PromptInputToken> allTokens = [];
        if (match.SlotTokens.TryGetValue(match.TailSlotSegmentIndex, out var capturedTokens)) {
            allTokens.AddRange(capturedTokens);
        }

        allTokens.Add(currentToken);
        routeMatch = EvaluateRouteMatch(
            context,
            state,
            scenario,
            alternative,
            tailSlot,
            allTokens,
            isPartial: true);
        if (!routeMatch.Compatible) {
            return false;
        }

        slot = tailSlot;
        segmentIndex = match.TailSlotSegmentIndex;
        return true;
    }

    private static RouteTokenMatch EvaluateRouteMatch(
        ResolvedPrompt context,
        PromptInputState state,
        PromptSurfaceScenario scenario,
        PromptAlternativeSpec alternative,
        PromptSlotSegmentSpec slot,
        IReadOnlyList<PromptInputToken> tokens,
        bool isPartial) {

        var rawText = string.Join(' ', tokens.Select(static token => token.Value));
        if (rawText.Length == 0) {
            return new RouteTokenMatch(
                Compatible: true,
                Specificity: ResolveDefaultRouteSpecificity(slot),
                Diagnostics: []);
        }

        if (ResolveAcceptedTokenRoute(slot).Length > 0 || IsExactTokenRoute(slot)) {
            var candidates = ResolveAcceptedTokenRoute(slot);
            var exact = candidates.Any(candidate => candidate.Equals(rawText, StringComparison.OrdinalIgnoreCase));
            var prefix = candidates.Any(candidate => candidate.StartsWith(rawText, StringComparison.OrdinalIgnoreCase));
            if (exact) {
                return new RouteTokenMatch(
                    Compatible: true,
                    Specificity: ResolveDefaultRouteSpecificity(slot) + 300 + rawText.Length,
                    Diagnostics: []);
            }

            if (isPartial && prefix) {
                return new RouteTokenMatch(
                    Compatible: true,
                    Specificity: ResolveDefaultRouteSpecificity(slot) + ResolveBestPrefixSpecificity(candidates, rawText),
                    Diagnostics: []);
            }

            return RouteTokenMatch.Incompatible([]);
        }

        if (IsBooleanRoute(slot)) {
            var exact = BooleanRouteTokens.Any(candidate => candidate.Equals(rawText, StringComparison.OrdinalIgnoreCase));
            var prefix = BooleanRouteTokens.Any(candidate => candidate.StartsWith(rawText, StringComparison.OrdinalIgnoreCase));
            if (exact) {
                return new RouteTokenMatch(true, ResolveDefaultRouteSpecificity(slot) + 280 + rawText.Length, []);
            }

            if (isPartial && prefix) {
                return new RouteTokenMatch(
                    true,
                    ResolveDefaultRouteSpecificity(slot) + ResolveBestPrefixSpecificity(BooleanRouteTokens, rawText),
                    []);
            }

            return RouteTokenMatch.Incompatible(["invalid: expected boolean"]);
        }

        if (IsTimeOnlyRoute(slot)) {
            if (TryParseWorldTime(rawText, out _)) {
                return new RouteTokenMatch(true, ResolveDefaultRouteSpecificity(slot) + 280 + rawText.Length, []);
            }

            if (isPartial && IsPotentialWorldTimePrefix(rawText)) {
                return new RouteTokenMatch(true, ResolveDefaultRouteSpecificity(slot) + rawText.Length, []);
            }

            return RouteTokenMatch.Incompatible(["invalid: expected hh:mm"]);
        }

        if (IsIntegerRoute(slot)) {
            if (int.TryParse(rawText, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) {
                return new RouteTokenMatch(true, ResolveDefaultRouteSpecificity(slot) + 260 + rawText.Length, []);
            }

            if (isPartial && IsPotentialIntegerPrefix(rawText)) {
                return new RouteTokenMatch(true, ResolveDefaultRouteSpecificity(slot) + rawText.Length, []);
            }

            return RouteTokenMatch.Incompatible(["invalid: expected integer"]);
        }

        if (TryExplainRouteValue(context, state, scenario, alternative, slot, rawText, out var explainResult)) {
            if (CommandPromptRoutes.IsSemanticLookupExact(slot.RouteMatchKind, slot.RouteSpecificity)) {
                if (explainResult.State == PromptParamExplainState.Resolved) {
                    return new RouteTokenMatch(
                        true,
                        ResolveDefaultRouteSpecificity(slot) + 300 + rawText.Length,
                        []);
                }

                if (isPartial && explainResult.State == PromptParamExplainState.Ambiguous) {
                    return new RouteTokenMatch(
                        true,
                        ResolveDefaultRouteSpecificity(slot) + 140 + rawText.Length,
                        []);
                }

                return explainResult.State == PromptParamExplainState.Invalid
                    ? RouteTokenMatch.Incompatible(["invalid"])
                    : RouteTokenMatch.Incompatible([]);
            }

            if (CommandPromptRoutes.IsSemanticLookupSoft(slot.RouteMatchKind, slot.RouteSpecificity)) {
                return explainResult.State switch {
                    PromptParamExplainState.Resolved => new RouteTokenMatch(
                        true,
                        ResolveDefaultRouteSpecificity(slot) + 240 + rawText.Length,
                        []),
                    PromptParamExplainState.Ambiguous => new RouteTokenMatch(
                        true,
                        ResolveDefaultRouteSpecificity(slot) + 120 + rawText.Length,
                        []),
                    PromptParamExplainState.Invalid => RouteTokenMatch.Incompatible(["invalid"]),
                    _ => new RouteTokenMatch(true, ResolveDefaultRouteSpecificity(slot) + rawText.Length, []),
                };
            }
        }

        if (IsFreeTextRoute(slot)) {
            return new RouteTokenMatch(true, ResolveDefaultRouteSpecificity(slot) + rawText.Length, []);
        }

        if (slot.ValidationMode == PromptSlotValidationMode.Integer) {
            if (int.TryParse(rawText, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) {
                return new RouteTokenMatch(true, ResolveDefaultRouteSpecificity(slot) + 220 + rawText.Length, []);
            }

            if (isPartial && IsPotentialIntegerPrefix(rawText)) {
                return new RouteTokenMatch(true, ResolveDefaultRouteSpecificity(slot) + rawText.Length, []);
            }

            return RouteTokenMatch.Incompatible(["invalid: expected integer"]);
        }

        return new RouteTokenMatch(true, ResolveDefaultRouteSpecificity(slot) + rawText.Length, []);
    }

    private static bool TryExplainRouteValue(
        ResolvedPrompt context,
        PromptInputState state,
        PromptSurfaceScenario scenario,
        PromptAlternativeSpec alternative,
        PromptSlotSegmentSpec slot,
        string rawText,
        out PromptParamExplainResult result) {
        result = PromptParamExplainResult.None;
        var allowUnquotedWhitespace = ResolveEffectiveConsumptionMode(slot) != PromptRouteConsumptionMode.SingleToken;
        PromptParamCandidateContext candidateContext = new(
            ResolveContext: new PromptResolveContext(
                Purpose: context.Purpose,
                State: new PromptInputState {
                    InputText = rawText,
                    CursorIndex = rawText.Length,
                },
                Scenario: scenario),
            Server: context.Server,
            ActiveAlternative: alternative,
            ActiveSlot: slot,
            EditTarget: new PromptEditTarget(
                StartIndex: 0,
                Length: rawText.Length,
                RawText: rawText,
                Quoted: false,
                LeadingCharacterEscaped: false,
                HasLeadingQuote: false,
                HasTrailingQuote: false,
                AllowUnquotedWhitespace: allowUnquotedWhitespace),
            RawText: rawText,
            RawInputText: rawText);
        if (PromptNestedPromptResolver.TryResolve(
            context,
            candidateContext,
            candidateContext.ResolveContext.State,
            candidateContext.EditTarget,
            out var nestedResolution)) {
            result = PromptNestedPromptResolver.BuildExplainResult(nestedResolution);
            return true;
        }

        if (slot.SemanticKey is not SemanticKey semanticKey) {
            return false;
        }

        var explainer = context.ResolveParameterExplainer(semanticKey);
        if (explainer is null) {
            return false;
        }

        try {
            return explainer.TryExplain(
                new PromptParamExplainContext(
                    ResolveContext: new PromptResolveContext(context.Purpose, state, scenario),
                    Server: context.Server,
                    ActiveAlternative: alternative,
                    ActiveSlot: slot,
                    EditTarget: new PromptEditTarget(0, rawText.Length, rawText, false, false, false, false, false),
                    RawText: rawText),
                out result);
        }
        catch {
            result = PromptParamExplainResult.None;
            return false;
        }
    }

    private static ImmutableArray<string> ResolveAcceptedTokenRoute(PromptSlotSegmentSpec slot) {
        return slot.RouteAcceptedTokens.Length > 0
            ? slot.RouteAcceptedTokens
            : slot.EnumCandidates.Length > 0
                ? slot.EnumCandidates
                : slot.AcceptedSpecialTokens;
    }

    private static IEnumerable<int> ResolveCompletedSpanLengths(
        PromptSlotSegmentSpec slot,
        int remainingTokenCount) {
        if (remainingTokenCount <= 0) {
            return [];
        }

        return ResolveEffectiveConsumptionMode(slot) switch {
            PromptRouteConsumptionMode.GreedyPhrase => Enumerable.Range(1, remainingTokenCount).Reverse(),
            PromptRouteConsumptionMode.RemainingText => [remainingTokenCount],
            PromptRouteConsumptionMode.Variadic => Enumerable.Range(1, remainingTokenCount).Reverse(),
            _ => [1],
        };
    }

    private static PromptRouteConsumptionMode ResolveEffectiveConsumptionMode(PromptSlotSegmentSpec slot) {
        if (slot.RouteConsumptionMode != PromptRouteConsumptionMode.SingleToken) {
            return slot.RouteConsumptionMode;
        }

        return slot.Cardinality == PromptSlotCardinality.Variadic
            ? PromptRouteConsumptionMode.Variadic
            : PromptRouteConsumptionMode.SingleToken;
    }

    private static bool CanTailContinue(PromptSlotSegmentSpec slot) {
        return ResolveEffectiveConsumptionMode(slot) != PromptRouteConsumptionMode.SingleToken;
    }

    private static bool ShouldStayOnTailSlotAfterSpace(PromptSlotSegmentSpec slot) {
        var mode = ResolveEffectiveConsumptionMode(slot);
        return mode is PromptRouteConsumptionMode.RemainingText
            or PromptRouteConsumptionMode.Variadic
            or PromptRouteConsumptionMode.GreedyPhrase;
    }

    private static bool IsExactTokenRoute(PromptSlotSegmentSpec slot) {
        return slot.RouteMatchKind == PromptRouteMatchKind.ExactTokenSet
            || slot.RouteAcceptedTokens.Length > 0
            || slot.EnumCandidates.Length > 0 && slot.RouteSpecificity >= CommandPromptRoutes.ExactTokenSetSpecificity;
    }

    private static bool IsIntegerRoute(PromptSlotSegmentSpec slot) {
        return slot.ValidationMode == PromptSlotValidationMode.Integer
            || slot.RouteMatchKind == PromptRouteMatchKind.Integer;
    }

    private static bool IsBooleanRoute(PromptSlotSegmentSpec slot) {
        return slot.RouteMatchKind == PromptRouteMatchKind.Boolean;
    }

    private static bool IsTimeOnlyRoute(PromptSlotSegmentSpec slot) {
        return slot.RouteMatchKind == PromptRouteMatchKind.TimeOnly;
    }

    private static bool IsFreeTextRoute(PromptSlotSegmentSpec slot) {
        return slot.RouteMatchKind == PromptRouteMatchKind.FreeText
            || slot.RouteSpecificity == 0
                && slot.EnumCandidates.Length == 0
                && slot.SemanticKey is null
                && slot.ValidationMode == PromptSlotValidationMode.None;
    }

    private static int ResolveDefaultRouteSpecificity(PromptSlotSegmentSpec slot) {
        return slot.RouteSpecificity > 0
            ? slot.RouteSpecificity
            : slot.RouteMatchKind switch {
                PromptRouteMatchKind.ExactTokenSet => CommandPromptRoutes.ExactTokenSetSpecificity,
                PromptRouteMatchKind.Integer => CommandPromptRoutes.IntegerSpecificity,
                PromptRouteMatchKind.Boolean => CommandPromptRoutes.BooleanSpecificity,
                PromptRouteMatchKind.TimeOnly => CommandPromptRoutes.TimeOnlySpecificity,
                PromptRouteMatchKind.SemanticLookupSoft => CommandPromptRoutes.SemanticLookupSoftSpecificity,
                PromptRouteMatchKind.SemanticLookupExact => CommandPromptRoutes.SemanticLookupExactSpecificity,
                _ => slot.ValidationMode == PromptSlotValidationMode.Integer
                    ? CommandPromptRoutes.IntegerSpecificity
                    : slot.EnumCandidates.Length > 0
                        ? CommandPromptRoutes.ExactTokenSetSpecificity
                        : CommandPromptRoutes.FreeTextSpecificity,
            };
    }

    private static int ResolveBestPrefixSpecificity(IEnumerable<string> candidates, string prefix) {
        return candidates
            .Where(candidate => candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(candidate => ResolvePrefixSpecificityScore(candidate, prefix))
            .DefaultIfEmpty(0)
            .Max();
    }

    private static bool IsPotentialIntegerPrefix(string rawText) {
        if (rawText.Length == 0) {
            return true;
        }

        if (rawText == "+" || rawText == "-") {
            return true;
        }

        var start = rawText[0] is '+' or '-' ? 1 : 0;
        if (start >= rawText.Length) {
            return true;
        }

        for (var index = start; index < rawText.Length; index++) {
            if (!char.IsDigit(rawText[index])) {
                return false;
            }
        }

        return true;
    }

    private static bool IsPotentialWorldTimePrefix(string rawText) {
        if (rawText.Length == 0) {
            return true;
        }

        var colonCount = 0;
        for (var index = 0; index < rawText.Length; index++) {
            var current = rawText[index];
            if (current == ':') {
                colonCount += 1;
                if (colonCount > 1) {
                    return false;
                }

                continue;
            }

            if (!char.IsDigit(current)) {
                return false;
            }
        }

        return true;
    }

    private static bool TryParseWorldTime(string rawText, out TimeOnly time) {
        time = default;
        var parts = rawText.Split(':');
        if (parts.Length != 2
            || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hours)
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes)
            || hours < 0 || hours > 23
            || minutes < 0 || minutes > 59) {
            return false;
        }

        time = new TimeOnly(hours, minutes);
        return true;
    }

    private static PromptRouteGuardBucket ResolveGuardBucket(
        ResolvedPrompt context,
        PromptAlternativeSpec alternative,
        PromptInputParseResult parse) {
        if (alternative.Metadata is not IPromptRouteGuardBucketSource guardBucketSource) {
            return PromptRouteGuardBucket.Empty;
        }

        return guardBucketSource.EvaluatePromptRouteGuardBucket(new PromptRouteGuardEvaluationContext(
            Alternative: alternative,
            InputText: parse.InputText,
            Tokens: parse.Tokens,
            EndsWithSpace: parse.HasTrailingSeparator,
            Server: context.Server));
    }

    private static string ResolveRouteSignatureKey(PromptSlotSegmentSpec slot) {
        var acceptedTokens = string.Join(",", ResolveAcceptedTokenRoute(slot).OrderBy(static token => token, StringComparer.OrdinalIgnoreCase));
        return string.Join("|", [
            ResolveEffectiveConsumptionMode(slot).ToString(),
            slot.RouteMatchKind.ToString(),
            ResolveDefaultRouteSpecificity(slot).ToString(CultureInfo.InvariantCulture),
            slot.ValidationMode.ToString(),
            slot.CompletionKindId?.Trim() ?? string.Empty,
            slot.SemanticKey?.Id ?? string.Empty,
            acceptedTokens,
        ]);
    }

    private static string ResolveRouteSignatureLabel(PromptSlotSegmentSpec slot) {
        if (!string.IsNullOrWhiteSpace(slot.DisplayLabel)) {
            return FormatParameterName(slot.DisplayLabel) ?? slot.DisplayLabel.Trim();
        }

        if (!string.IsNullOrWhiteSpace(slot.Name)) {
            return FormatParameterName(slot.Name) ?? slot.Name.Trim();
        }

        if (slot.SemanticKey is SemanticKey semanticKey && !string.IsNullOrWhiteSpace(semanticKey.DisplayName)) {
            return semanticKey.DisplayName.Trim();
        }

        var accepted = ResolveAcceptedTokenRoute(slot);
        if (accepted.Length > 0 && accepted.Length <= 3) {
            return string.Join('|', accepted);
        }

        var completionKindId = slot.CompletionKindId?.Trim();
        if (string.Equals(completionKindId, PromptSuggestionKindIds.Boolean, StringComparison.Ordinal)) {
            return "bool";
        }

        if (string.Equals(completionKindId, PromptSuggestionKindIds.Enum, StringComparison.Ordinal)) {
            return "enum";
        }

        return IsTimeOnlyRoute(slot)
            ? "hh:mm"
            : IsIntegerRoute(slot)
                ? "int"
                : "arg";
    }

    private static string? FormatParameterName(string? name) {
        if (string.IsNullOrWhiteSpace(name)) {
            return null;
        }

        var value = name.Trim().Replace('-', ' ').Replace('_', ' ');
        System.Text.StringBuilder builder = new(value.Length + 4);
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
                    || char.IsUpper(previous)
                        && index + 1 < value.Length
                        && char.IsLower(value[index + 1]));
                var splitDigitBoundary = char.IsDigit(current) && char.IsLetter(previous)
                    || char.IsLetter(current) && char.IsDigit(previous);
                if (splitCamelCase || splitDigitBoundary) {
                    builder.Append(' ');
                }
            }

            builder.Append(char.ToLowerInvariant(current));
        }

        var normalized = builder.ToString().Trim();
        return normalized.Length == 0 ? null : normalized;
    }

    private static PromptExpectation ResolveAcceptExpectation(
        PromptAlternativeSpec alternative,
        int segmentIndex,
        PromptInputToken? currentToken,
        IReadOnlyDictionary<string, List<PromptInputToken>> namedAssignments,
        IReadOnlyDictionary<string, PromptSlotSegmentSpec> slotsByName,
        IReadOnlySet<string> recognizedModifierKeys,
        IReadOnlyList<PromptModifierOptionSpec> availableModifiers,
        PromptNamedOptionSpec? pendingNamedOption,
        PromptModifierOptionSpec? exactModifierMatch,
        PromptNamedOptionSpec? exactNamedOption) {
        if (pendingNamedOption is PromptNamedOptionSpec pendingOption
            && slotsByName.TryGetValue(pendingOption.SlotName, out var pendingSlot)) {
            return new PromptExpectation(PromptFocusKind.Slot, pendingSlot, segmentIndex, []);
        }

        if (exactNamedOption is PromptNamedOptionSpec currentOption
            && slotsByName.TryGetValue(currentOption.SlotName, out var namedSlot)) {
            return new PromptExpectation(PromptFocusKind.Slot, namedSlot, segmentIndex, []);
        }

        return BuildExpectation(
            alternative,
            segmentIndex,
            exactModifierMatch is null ? currentToken : null,
            namedAssignments,
            availableModifiers);
    }

    private static PromptExpectation BuildExpectation(
        PromptAlternativeSpec alternative,
        int segmentIndex,
        PromptInputToken? currentToken,
        IReadOnlyDictionary<string, List<PromptInputToken>> namedAssignments,
        IReadOnlyList<PromptModifierOptionSpec> availableModifiers) {
        var effectiveSegmentIndex = SkipSatisfiedSegments(alternative.Segments, segmentIndex, currentToken, namedAssignments);
        if (currentToken is PromptInputToken rawCurrent && IsModifierPrefixToken(rawCurrent, availableModifiers)) {
            return new PromptExpectation(PromptFocusKind.Modifier, null, effectiveSegmentIndex, []);
        }

        if (effectiveSegmentIndex >= alternative.Segments.Length) {
            var terminalKind = currentToken is null || alternative.OverflowBehavior == PromptOverflowBehavior.IgnoreAdditionalTokens
                ? PromptFocusKind.None
                : PromptFocusKind.Overflow;
            return new PromptExpectation(terminalKind, null, -1, []);
        }

        if (alternative.Segments[effectiveSegmentIndex] is PromptLiteralSegmentSpec literalSegment) {
            return new PromptExpectation(PromptFocusKind.Literal, null, effectiveSegmentIndex, [literalSegment.Value]);
        }

        return new PromptExpectation(
            PromptFocusKind.Slot,
            (PromptSlotSegmentSpec)alternative.Segments[effectiveSegmentIndex],
            effectiveSegmentIndex,
            []);
    }

    private static PromptLiteralSegmentSpec? ResolveExactLiteralSegment(
        PromptAlternativeSpec alternative,
        int segmentIndex,
        PromptInputToken? currentToken) {
        if (currentToken is not PromptInputToken token
            || ResolveLiteralSegment(alternative, segmentIndex) is not PromptLiteralSegmentSpec literalSegment) {
            return null;
        }

        return token.Value.Equals(literalSegment.Value, StringComparison.OrdinalIgnoreCase)
            ? literalSegment
            : null;
    }

    private static PromptLiteralSegmentSpec? ResolveLiteralSegment(PromptAlternativeSpec alternative, int segmentIndex) {
        return segmentIndex >= 0
            && segmentIndex < alternative.Segments.Length
            && alternative.Segments[segmentIndex] is PromptLiteralSegmentSpec literalSegment
                ? literalSegment
                : null;
    }

    private static int SkipSatisfiedSegments(
        IReadOnlyList<PromptSegmentSpec> segments,
        int segmentIndex,
        PromptInputToken? nextToken,
        IReadOnlyDictionary<string, List<PromptInputToken>> namedAssignments) {
        var index = segmentIndex;
        while (index < segments.Count) {
            if (segments[index] is not PromptSlotSegmentSpec slot) {
                break;
            }

            if (!string.IsNullOrWhiteSpace(slot.Name) && namedAssignments.ContainsKey(slot.Name)) {
                index += 1;
                continue;
            }

            if (slot.Cardinality == PromptSlotCardinality.Optional
                && nextToken is PromptInputToken token
                && index + 1 < segments.Count
                && segments[index + 1] is PromptLiteralSegmentSpec nextLiteral
                && token.Value.Equals(nextLiteral.Value, StringComparison.OrdinalIgnoreCase)) {
                index += 1;
                continue;
            }

            break;
        }

        return index;
    }

    private static bool TryConsumeModifierToken(
        PromptAlternativeSpec alternative,
        int segmentIndex,
        PromptInputToken token,
        out string? modifierIdentity) {
        modifierIdentity = null;
        if (token.Quoted || token.LeadingCharacterEscaped || !token.Value.StartsWith("-", StringComparison.Ordinal)) {
            return false;
        }

        foreach (var option in ResolveModifierOptions(alternative, segmentIndex)) {
            if (!option.Tokens.Any(candidate => candidate.Equals(token.Value, StringComparison.OrdinalIgnoreCase))) {
                continue;
            }

            modifierIdentity = ResolveModifierIdentity(option);
            return true;
        }

        return false;
    }

    private static bool TryConsumeNamedOption(
        PromptAlternativeSpec alternative,
        int segmentIndex,
        PromptInputToken token,
        out PromptNamedOptionSpec? pendingNamedOption) {
        pendingNamedOption = null;
        if (token.Quoted || token.LeadingCharacterEscaped || !token.Value.StartsWith("-", StringComparison.Ordinal)) {
            return false;
        }

        foreach (var option in ResolveNamedOptions(alternative, segmentIndex)) {
            if (!option.Tokens.Any(candidate => candidate.Equals(token.Value, StringComparison.OrdinalIgnoreCase))) {
                continue;
            }

            pendingNamedOption = option;
            return true;
        }

        return false;
    }

    private static bool ShouldExcludeRecognizedTokenFromSlotFlow(
        PromptAlternativeSpec alternative,
        int segmentIndex,
        PromptInputToken token) {
        if (token.Quoted || token.LeadingCharacterEscaped || !token.Value.StartsWith("-", StringComparison.Ordinal)) {
            return false;
        }

        foreach (var shim in alternative.Shims) {
            if (shim.StartSegmentIndex > segmentIndex) {
                continue;
            }

            switch (shim) {
                case PromptModifierShimSpec modifierShim when modifierShim.ExcludeRecognizedModifiersFromSlotFlow
                    && modifierShim.Options.Any(option => option.Tokens.Any(candidate => candidate.Equals(token.Value, StringComparison.OrdinalIgnoreCase))):
                    return true;
                case PromptFlagSetShimSpec flagShim when flagShim.ExcludeRecognizedFlagsFromSlotFlow
                    && flagShim.Options.Any(option => option.Tokens.Any(candidate => candidate.Equals(token.Value, StringComparison.OrdinalIgnoreCase))):
                    return true;
                case PromptNamedOptionShimSpec namedOptionShim when namedOptionShim.Options.Any(option =>
                    option.Tokens.Any(candidate => candidate.Equals(token.Value, StringComparison.OrdinalIgnoreCase))):
                    return true;
            }
        }

        return false;
    }

    private static PromptModifierOptionSpec? ResolveExactModifierMatch(
        PromptAlternativeSpec alternative,
        int segmentIndex,
        PromptInputToken? currentToken) {
        if (currentToken is not PromptInputToken token
            || token.Quoted
            || token.LeadingCharacterEscaped
            || !token.Value.StartsWith('-')) {
            return null;
        }

        foreach (var option in ResolveModifierOptions(alternative, segmentIndex)) {
            if (option.Tokens.Any(candidate => candidate.Equals(token.Value, StringComparison.OrdinalIgnoreCase))) {
                return option;
            }
        }

        return null;
    }

    private static PromptNamedOptionSpec? ResolveExactNamedOption(
        PromptAlternativeSpec alternative,
        int segmentIndex,
        PromptInputToken? currentToken) {
        if (currentToken is not PromptInputToken token
            || token.Quoted
            || token.LeadingCharacterEscaped
            || !token.Value.StartsWith('-')) {
            return null;
        }

        foreach (var option in ResolveNamedOptions(alternative, segmentIndex)) {
            if (option.Tokens.Any(candidate => candidate.Equals(token.Value, StringComparison.OrdinalIgnoreCase))) {
                return option;
            }
        }

        return null;
    }

    private static List<PromptModifierOptionSpec> ResolveAvailableModifiers(
        PromptAlternativeSpec alternative,
        int segmentIndex,
        IReadOnlySet<string> recognizedModifierKeys,
        PromptInputToken? currentToken) {

        List<PromptModifierOptionSpec> available = [];
        foreach (var option in ResolveModifierOptions(alternative, segmentIndex)) {
            if (!recognizedModifierKeys.Contains(ResolveModifierIdentity(option))) {
                available.Add(option);
            }
        }

        var namedOptions = ResolveNamedOptions(alternative, segmentIndex);
        if (currentToken is not PromptInputToken current
            || current.Quoted
            || current.LeadingCharacterEscaped
            || !current.Value.StartsWith("-", StringComparison.Ordinal)) {
            available.AddRange(namedOptions.Select(static option => new PromptModifierOptionSpec {
                Key = option.Key,
                CanonicalToken = option.CanonicalToken,
                Tokens = option.Tokens,
            }));
            return available;
        }

        available.AddRange(namedOptions
            .Where(option => option.Tokens.Any(token => token.StartsWith(current.Value, StringComparison.OrdinalIgnoreCase)))
            .Select(static option => new PromptModifierOptionSpec {
                Key = option.Key,
                CanonicalToken = option.CanonicalToken,
                Tokens = option.Tokens,
            }));
        return available;
    }

    private static bool IsModifierPrefixToken(PromptInputToken token, IReadOnlyList<PromptModifierOptionSpec> availableModifiers) {
        if (token.Quoted || token.LeadingCharacterEscaped || !token.Value.StartsWith("-", StringComparison.Ordinal)) {
            return false;
        }

        return availableModifiers.Any(option => option.Tokens.Any(candidate =>
            candidate.Equals(token.Value, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(token.Value, StringComparison.OrdinalIgnoreCase)));
    }

    private static List<PromptModifierOptionSpec> ResolveModifierOptions(PromptAlternativeSpec alternative, int segmentIndex) {
        List<PromptModifierOptionSpec> options = [];
        foreach (var shim in alternative.Shims) {
            if (shim.StartSegmentIndex > segmentIndex) {
                continue;
            }

            switch (shim) {
                case PromptModifierShimSpec modifierShim:
                    options.AddRange(modifierShim.Options);
                    break;
                case PromptFlagSetShimSpec flagShim:
                    options.AddRange(flagShim.Options);
                    break;
            }
        }

        return options;
    }

    private static IEnumerable<PromptNamedOptionSpec> ResolveNamedOptions(PromptAlternativeSpec alternative, int segmentIndex) {
        return alternative.Shims
            .OfType<PromptNamedOptionShimSpec>()
            .Where(shim => shim.StartSegmentIndex <= segmentIndex)
            .SelectMany(static shim => shim.Options);
    }

    private static List<PromptModifierOptionSpec> ResolveAllModifiers(PromptAlternativeSpec alternative) {
        var modifiers = ResolveModifierOptions(alternative, int.MaxValue);
        modifiers.AddRange(ResolveNamedOptions(alternative, int.MaxValue).Select(static option => new PromptModifierOptionSpec {
            Key = option.Key,
            CanonicalToken = option.CanonicalToken,
            Tokens = option.Tokens,
        }));
        return modifiers;
    }

    private static List<PromptModifierOptionSpec> ResolveRecognizedFlags(
        PromptAlternativeSpec alternative,
        IReadOnlySet<string> recognizedModifierKeys) {
        List<PromptModifierOptionSpec> recognized = [];
        foreach (var shim in alternative.Shims.OfType<PromptFlagSetShimSpec>()) {
            foreach (var option in shim.Options) {
                if (recognizedModifierKeys.Contains(ResolveModifierIdentity(option))) {
                    recognized.Add(option);
                }
            }
        }

        return recognized;
    }

    private static string ResolveModifierIdentity(PromptModifierOptionSpec option) {
        if (!string.IsNullOrWhiteSpace(option.Key)) {
            return option.Key.Trim();
        }

        if (!string.IsNullOrWhiteSpace(option.CanonicalToken)) {
            return option.CanonicalToken.Trim();
        }

        return option.Tokens.FirstOrDefault(static token => !string.IsNullOrWhiteSpace(token)) ?? string.Empty;
    }

    private static PromptEditTarget BuildEditTarget(
        PromptInputParseResult parse,
        PromptInputView inputView,
        PromptSlotSegmentSpec? activeSlot,
        IReadOnlyDictionary<int, List<PromptInputToken>> slotTokens,
        int activeSegmentIndex) {
        PromptInputToken? currentToken = inputView.LiveToken;
        if (activeSlot is PromptSlotSegmentSpec multiTokenSlot
            && ResolveEffectiveConsumptionMode(multiTokenSlot) != PromptRouteConsumptionMode.SingleToken) {
            if (TryBuildPlainContinuationEditTarget(parse, inputView, slotTokens, activeSegmentIndex, out var plainContinuationTarget)) {
                return plainContinuationTarget;
            }

            if (TryBuildDecoratedContinuationEditTarget(parse, inputView, slotTokens, activeSegmentIndex, out var decoratedContinuationTarget)) {
                return decoratedContinuationTarget;
            }
        }

        bool allowUnquotedWhitespace = activeSlot is PromptSlotSegmentSpec slot
            && ResolveEffectiveConsumptionMode(slot) != PromptRouteConsumptionMode.SingleToken;
        if (currentToken is PromptInputToken token) {
            return new PromptEditTarget(
                StartIndex: token.StartIndex,
                Length: token.SourceLength,
                RawText: token.Value,
                Quoted: token.Quoted,
                LeadingCharacterEscaped: token.LeadingCharacterEscaped,
                HasLeadingQuote: HasQuoteBefore(parse.InputText, token.StartIndex),
                HasTrailingQuote: HasQuoteAt(parse.InputText, token.EndIndex),
                AllowUnquotedWhitespace: allowUnquotedWhitespace);
        }

        var startIndex = Math.Clamp(inputView.LiveTextStart, 0, parse.InputText.Length);
        return new PromptEditTarget(
            StartIndex: startIndex,
            Length: 0,
            RawText: inputView.LiveText,
            Quoted: inputView.LiveTextQuoted,
            LeadingCharacterEscaped: inputView.LiveTextLeadingCharacterEscaped,
            HasLeadingQuote: HasQuoteBefore(parse.InputText, startIndex),
            HasTrailingQuote: HasQuoteAt(parse.InputText, startIndex),
            AllowUnquotedWhitespace: allowUnquotedWhitespace);
    }

    private static PromptEditTarget BuildSlotInputTarget(
        PromptInputParseResult parse,
        PromptInputView inputView,
        IReadOnlyDictionary<int, List<PromptInputToken>> slotTokens,
        int activeSegmentIndex,
        PromptEditTarget fallbackTarget) {
        // Nested prompt providers need the full logical slot input span, not just the token that
        // is currently being edited. That includes a committed trailing separator when the outer
        // slot has opened the next continuation token. If we trim that space away here, nested
        // GreedyPhrase/RemainingText prompts fall back to "still editing the previous token" and
        // regress from `Melvin ` -> `Melvin IX` back to `Melvin ` -> `Melvin`.
        //
        // Reusing the completion edit target here also breaks recursive slots as soon as quoting
        // forces the editor back to single-token replacement mode.
        if (activeSegmentIndex < 0) {
            return fallbackTarget;
        }

        List<PromptInputToken> tokens = [];
        if (slotTokens.TryGetValue(activeSegmentIndex, out var capturedTokens)) {
            tokens.AddRange(capturedTokens);
        }

        if (inputView.LiveToken is PromptInputToken currentToken) {
            tokens.Add(currentToken);
        }

        if (tokens.Count == 0) {
            return fallbackTarget;
        }

        var first = tokens[0];
        var last = tokens[^1];
        var startIndex = first.StartIndex;
        var hasLeadingQuote = first.Quoted && HasQuoteBefore(parse.InputText, first.StartIndex);
        if (hasLeadingQuote) {
            startIndex -= 1;
        }

        var endIndex = inputView.LiveToken is null && inputView.HasTrailingSeparator
            ? parse.InputText.Length
            : last.EndIndex + (last.Quoted && HasQuoteAt(parse.InputText, last.EndIndex) ? 1 : 0);
        if (startIndex < 0 || endIndex < startIndex || endIndex > parse.InputText.Length) {
            return fallbackTarget;
        }

        var result = new PromptEditTarget(
            StartIndex: startIndex,
            Length: endIndex - startIndex,
            RawText: parse.InputText[startIndex..endIndex],
            Quoted: first.Quoted && tokens.Count == 1,
            LeadingCharacterEscaped: first.LeadingCharacterEscaped && tokens.Count == 1,
            HasLeadingQuote: hasLeadingQuote,
            HasTrailingQuote: last.Quoted && HasQuoteAt(parse.InputText, last.EndIndex),
            AllowUnquotedWhitespace: fallbackTarget.AllowUnquotedWhitespace);
        return result;
    }

    private static bool TryBuildPlainContinuationEditTarget(
        PromptInputParseResult parse,
        PromptInputView inputView,
        IReadOnlyDictionary<int, List<PromptInputToken>> slotTokens,
        int activeSegmentIndex,
        out PromptEditTarget target) {
        target = default;
        if (activeSegmentIndex < 0) {
            return false;
        }

        PromptInputToken? currentToken = inputView.LiveToken;
        List<PromptInputToken> tokens = [];
        if (slotTokens.TryGetValue(activeSegmentIndex, out var capturedTokens)) {
            tokens.AddRange(capturedTokens);
        }

        if (currentToken is PromptInputToken current) {
            tokens.Add(current);
        }

        // RemainingText/Variadic/GreedyPhrase should edit the whole plain-text span as one unit.
        // If we only target the last token, ghost preview re-inserts the full candidate and the
        // presentation analysis falls through to a duplicated-input overflow path.
        if (tokens.Count == 0
            || tokens.Any(static token => token.Quoted || token.LeadingCharacterEscaped)) {
            return false;
        }

        PromptInputToken first = tokens[0];
        PromptInputToken last = tokens[^1];
        if (HasQuoteBefore(parse.InputText, first.StartIndex)
            || HasQuoteAt(parse.InputText, last.EndIndex)) {
            return false;
        }

        string rawText = string.Join(' ', tokens.Select(static token => token.Value));
        int startIndex = first.StartIndex;
        int endIndex = currentToken is null && inputView.HasTrailingSeparator
            ? parse.TailTextStart
            : last.EndIndex;
        if (endIndex < startIndex) {
            return false;
        }

        if (currentToken is null && inputView.HasTrailingSeparator) {
            rawText += " ";
        }

        target = new PromptEditTarget(
            StartIndex: startIndex,
            Length: endIndex - startIndex,
            RawText: rawText,
            Quoted: false,
            LeadingCharacterEscaped: false,
            HasLeadingQuote: false,
            HasTrailingQuote: false,
            AllowUnquotedWhitespace: true);
        return true;
    }

    private static bool TryBuildDecoratedContinuationEditTarget(
        PromptInputParseResult parse,
        PromptInputView inputView,
        IReadOnlyDictionary<int, List<PromptInputToken>> slotTokens,
        int activeSegmentIndex,
        out PromptEditTarget target) {
        target = default;
        if (activeSegmentIndex < 0
            || !slotTokens.TryGetValue(activeSegmentIndex, out var capturedTokens)
            || capturedTokens.Count == 0
            || inputView.LiveToken is null && !inputView.HasTrailingSeparator) {
            return false;
        }

        List<PromptInputToken> allTokens = [.. capturedTokens];
        if (inputView.LiveToken is PromptInputToken currentToken) {
            allTokens.Add(currentToken);
        }

        bool hasDecoratedToken = capturedTokens.Any(static token => token.Quoted || token.LeadingCharacterEscaped);
        if (!hasDecoratedToken
            && !HasQuoteBefore(parse.InputText, capturedTokens[0].StartIndex)
            && !HasQuoteAt(parse.InputText, capturedTokens[^1].EndIndex)) {
            return false;
        }

        string rawText = string.Join(' ', allTokens.Select(static token => token.Value));
        if (rawText.Length == 0) {
            return false;
        }

        if (inputView.LiveToken is PromptInputToken liveToken) {
            // The logical slot text still spans the decorated prefix plus the live tail token,
            // but the actual source edit should stay anchored to the live token so ghost preview
            // only replaces the part the operator is currently typing.
            target = new PromptEditTarget(
                StartIndex: liveToken.StartIndex,
                Length: liveToken.SourceLength,
                RawText: rawText,
                Quoted: liveToken.Quoted,
                LeadingCharacterEscaped: liveToken.LeadingCharacterEscaped,
                HasLeadingQuote: HasQuoteBefore(parse.InputText, liveToken.StartIndex),
                HasTrailingQuote: HasQuoteAt(parse.InputText, liveToken.EndIndex),
                AllowUnquotedWhitespace: true);
            return true;
        }

        if (!inputView.HasTrailingSeparator) {
            return false;
        }

        if (rawText.Length == 0) {
            return false;
        }

        // Once a multi-token slot already contains quoted or escaped source text, replacing the
        // entire source span would rewrite earlier input just to append the continuation suffix.
        // Keep the logical slot prefix for candidate matching, but point the edit at the fresh
        // cursor position so `/ai "Melvin" ` can ghost only `IX` while still analyzing as the
        // same RemainingText slot.
        target = new PromptEditTarget(
            StartIndex: parse.TailTextStart,
            Length: 0,
            RawText: rawText + " ",
            Quoted: false,
            LeadingCharacterEscaped: false,
            HasLeadingQuote: false,
            HasTrailingQuote: false,
            AllowUnquotedWhitespace: true);
        return true;
    }

    private static string ResolveRouteStyleId(bool compatible) {
        return compatible ? PromptStyleKeys.SyntaxValue : string.Empty;
    }

    private static bool HasQuoteBefore(string input, int startIndex) {
        return startIndex > 0
            && startIndex - 1 < input.Length
            && input[startIndex - 1] == '"';
    }

    private static bool HasQuoteAt(string input, int index) {
        return index >= 0
            && index < input.Length
            && input[index] == '"';
    }

    private static int CountRemainingRequiredSegments(
        IReadOnlyList<PromptSegmentSpec> segments,
        int startIndex,
        IReadOnlyDictionary<string, List<PromptInputToken>> namedAssignments) {
        if (startIndex < 0) {
            return 0;
        }

        var required = 0;
        for (var index = startIndex; index < segments.Count; index++) {
            switch (segments[index]) {
                case PromptLiteralSegmentSpec:
                    required += 1;
                    break;
                case PromptSlotSegmentSpec slot when !string.IsNullOrWhiteSpace(slot.Name)
                    && namedAssignments.ContainsKey(slot.Name):
                    break;
                case PromptSlotSegmentSpec slot when slot.Cardinality == PromptSlotCardinality.Optional:
                    break;
                case PromptSlotSegmentSpec:
                    required += 1;
                    break;
            }
        }

        return required;
    }

    private static int ResolveRemainingShapeStartIndex(PromptFocusKind focusKind, int activeSegmentIndex, int fallbackSegmentIndex) {
        return focusKind switch {
            PromptFocusKind.Literal or PromptFocusKind.Slot => activeSegmentIndex,
            PromptFocusKind.Modifier => fallbackSegmentIndex,
            PromptFocusKind.None => -1,
            _ => fallbackSegmentIndex,
        };
    }

    private static int ResolvePrefixSpecificityScore(string candidate, string prefix) {
        if (prefix.Length == 0 || !candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) {
            return 0;
        }

        if (candidate.Equals(prefix, StringComparison.OrdinalIgnoreCase)) {
            return 512;
        }

        var remainingLength = Math.Max(0, candidate.Length - prefix.Length);
        return Math.Max(1, 256 - Math.Min(255, remainingLength));
    }

    private static int ResolveSegmentIndex(PromptAlternativeSpec alternative, PromptSlotSegmentSpec slot) {
        for (var index = 0; index < alternative.Segments.Length; index++) {
            if (ReferenceEquals(alternative.Segments[index], slot)) {
                return index;
            }
        }

        if (!string.IsNullOrWhiteSpace(slot.Name)) {
            for (var index = 0; index < alternative.Segments.Length; index++) {
                if (alternative.Segments[index] is PromptSlotSegmentSpec candidate
                    && candidate.Name.Equals(slot.Name, StringComparison.OrdinalIgnoreCase)) {
                    return index;
                }
            }
        }

        return -1;
    }

    private static PromptSlotSegmentSpec? ResolveSlot(PromptAlternativeSpec alternative, int segmentIndex) {
        return segmentIndex >= 0
            && segmentIndex < alternative.Segments.Length
            && alternative.Segments[segmentIndex] is PromptSlotSegmentSpec slot
                ? slot
                : null;
    }

    private readonly record struct RouteTokenMatch(
        bool Compatible,
        int Specificity,
        ImmutableArray<string> Diagnostics)
    {
        public static RouteTokenMatch Incompatible(IEnumerable<string> diagnostics) {
            return new RouteTokenMatch(
                Compatible: false,
                Specificity: 0,
                Diagnostics: [.. diagnostics.Where(static item => !string.IsNullOrWhiteSpace(item))]);
        }
    }

    private readonly record struct PromptInputView(
        int CommittedTokenCount,
        PromptInputToken? LiveToken,
        string LiveText,
        int LiveTextStart,
        bool LiveTextQuoted,
        bool LiveTextLeadingCharacterEscaped,
        bool HasTrailingSeparator);

    private readonly record struct InterpretationResolution(
        ImmutableArray<AlternativeEvaluation> RankedEvaluations,
        AlternativeEvaluation ActiveEvaluation,
        ImmutableArray<AlternativeEvaluation> ActiveInterpretationEvaluations,
        PromptInterpretationState InterpretationState);

    private readonly record struct InterpretationCandidate(string Id, ImmutableArray<AlternativeEvaluation> Evaluations)
    {
        public AlternativeEvaluation BestEvaluation => Evaluations[0];
    }

    private readonly record struct AlternativeEvaluation(
        PromptAlternativeSpec Alternative,
        int AlternativeIndex,
        bool Compatible,
        bool RouteCompatible,
        PromptRouteGuardState GuardState,
        string GuardBucketKey,
        string GuardBucketLabel,
        string RouteSignatureKey,
        string RouteSignatureLabel,
        PromptFocusKind FocusKind,
        PromptExpectation AcceptExpectation,
        PromptSlotSegmentSpec? ActiveSlot,
        int ActiveSegmentIndex,
        PromptEditTarget EditTarget,
        PromptEditTarget SlotInputTarget,
        ImmutableArray<string> ExpectedLiterals,
        ImmutableArray<PromptModifierOptionSpec> AvailableModifiers,
        ImmutableArray<PromptModifierOptionSpec> AllModifiers,
        ImmutableArray<PromptModifierOptionSpec> RecognizedFlags,
        ImmutableArray<PromptHighlightSpan> HighlightSpans,
        ImmutableArray<string> Diagnostics,
        int PathSpecificity,
        int CurrentSlotSpecificity,
        bool PreferTrailingSeparatorCommit,
        int RemainingRequiredShapes)
    {
        public string InterpretationId => string.IsNullOrWhiteSpace(RouteSignatureKey) && string.IsNullOrWhiteSpace(GuardBucketKey)
            ? "prompt-interpretation:alt:" + Alternative.AlternativeId
            : "prompt-interpretation:" + RouteSignatureKey + "|" + GuardBucketKey;

        public static AlternativeEvaluation Incompatible(PromptAlternativeSpec alternative, int alternativeIndex) {
            return new AlternativeEvaluation(
                Alternative: alternative,
                AlternativeIndex: alternativeIndex,
                Compatible: false,
                RouteCompatible: false,
                GuardState: PromptRouteGuardState.Unknown,
                GuardBucketKey: string.Empty,
                GuardBucketLabel: string.Empty,
                RouteSignatureKey: string.Empty,
                RouteSignatureLabel: string.Empty,
                FocusKind: PromptFocusKind.None,
                AcceptExpectation: new PromptExpectation(PromptFocusKind.None, null, -1, []),
                ActiveSlot: null,
                ActiveSegmentIndex: -1,
                EditTarget: default,
                SlotInputTarget: default,
                ExpectedLiterals: [],
                AvailableModifiers: [],
                AllModifiers: [],
                RecognizedFlags: [],
                HighlightSpans: [],
                Diagnostics: [],
                PathSpecificity: int.MinValue,
                CurrentSlotSpecificity: 0,
                PreferTrailingSeparatorCommit: false,
                RemainingRequiredShapes: int.MaxValue);
        }
    }

    private sealed class CompletedMatchState
    {
        public int SegmentIndex { get; set; }
        public int TailSlotSegmentIndex { get; set; } = -1;

        public PromptNamedOptionSpec? PendingNamedOption { get; set; }

        public Dictionary<int, List<PromptInputToken>> SlotTokens { get; } = [];
        public Dictionary<string, List<PromptInputToken>> NamedAssignments { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> RecognizedModifierKeys { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<PromptHighlightSpan> Highlights { get; } = [];
        public int LiteralMatches { get; set; }
        public int ConsumedOrderedTokens { get; set; }
        public int CompletedPathSpecificity { get; set; }

        public CompletedMatchState Clone() {
            CompletedMatchState clone = new() {
                SegmentIndex = SegmentIndex,
                TailSlotSegmentIndex = TailSlotSegmentIndex,
                PendingNamedOption = PendingNamedOption,
                LiteralMatches = LiteralMatches,
                ConsumedOrderedTokens = ConsumedOrderedTokens,
                CompletedPathSpecificity = CompletedPathSpecificity,
            };
            foreach ((var key, var value) in SlotTokens) {
                clone.SlotTokens[key] = [.. value];
            }

            foreach ((var key, var value) in NamedAssignments) {
                clone.NamedAssignments[key] = [.. value];
            }

            clone.RecognizedModifierKeys.UnionWith(RecognizedModifierKeys);
            clone.Highlights.AddRange(Highlights);
            return clone;
        }
    }
}
