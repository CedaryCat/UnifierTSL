using UnifierTSL.Surface.Prompting.Model;
using UnifierTSL.Commanding;
using UnifierTSL.Surface.Prompting.Runtime;

namespace UnifierTSL.Surface.Prompting.Semantics
{
    internal readonly record struct NestedPromptResolution(
        ResolvedPrompt Context,
        PromptInputState State,
        PromptSemanticAnalysis Analysis,
        PromptEditTarget OuterEditTarget);

    internal static class PromptNestedPromptResolver
    {
        public static bool TryResolve(
            ResolvedPrompt outerContext,
            PromptSemanticAnalysis outerAnalysis,
            PromptInputState outerState,
            PromptSurfaceScenario scenario,
            out NestedPromptResolution resolution) {
            resolution = default;
            if (!PromptSemanticCompletion.TryCreateCandidateContext(
                outerContext,
                outerAnalysis,
                outerState,
                scenario,
                out var candidateContext)) {
                return false;
            }
            return TryResolve(
                outerContext,
                candidateContext,
                CreateNestedState(outerState, outerAnalysis.InterpretationState, outerAnalysis.SlotInputTarget),
                outerAnalysis.SlotInputTarget,
                out resolution);
        }

        public static bool TryResolve(
            ResolvedPrompt outerContext,
            PromptParamCandidateContext candidateContext,
            PromptInputState nestedState,
            PromptEditTarget outerEditTarget,
            out NestedPromptResolution resolution) {
            resolution = default;
            if (candidateContext.ActiveSlot.SemanticKey is not SemanticKey semanticKey
                || outerContext.ResolveParameterCandidateProvider(semanticKey) is not IParamValueNestedPromptProvider provider
                || !provider.TryCreateNestedPrompt(candidateContext, out var semanticPrompt)
                || semanticPrompt.Alternatives.Length == 0) {
                return false;
            }

            var nestedContext = new ResolvedPrompt {
                Purpose = outerContext.Purpose,
                Server = outerContext.Server,
                SemanticSpec = semanticPrompt,
                Candidates = outerContext.Candidates,
                ParameterExplainers = outerContext.ParameterExplainers,
                ParameterCandidateProviders = outerContext.ParameterCandidateProviders,
            };

            var nestedAnalysis = PromptSemanticAnalyzer.TryAnalyze(
                nestedContext,
                nestedState,
                candidateContext.ResolveContext.Scenario);
            if (nestedAnalysis is not PromptSemanticAnalysis analysis) {
                return false;
            }
            resolution = new NestedPromptResolution(nestedContext, nestedState, analysis, outerEditTarget);
            return true;
        }

        public static PromptParamExplainResult BuildExplainResult(NestedPromptResolution resolution) {
            var alternatives = resolution.Analysis.CompatibleAlternatives
                .Select(static alternative => alternative.AlternativeId)
                .Distinct(StringComparer.Ordinal)
                .Count();
            if (alternatives > 1) {
                var preview = string.Join(", ", resolution.Analysis.CompatibleAlternatives
                    .Select(ResolveDisplayText)
                    .Where(static text => !string.IsNullOrWhiteSpace(text))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(3));
                if (string.IsNullOrWhiteSpace(preview)) {
                    return new PromptParamExplainResult(PromptParamExplainState.Ambiguous, "ambiguous");
                }

                if (alternatives > 3) {
                    preview += ", ...";
                }

                return new PromptParamExplainResult(PromptParamExplainState.Ambiguous, "ambiguous: " + preview);
            }

            return new PromptParamExplainResult(
                PromptParamExplainState.Resolved,
                ResolveDisplayText(resolution.Analysis.ActiveAlternative));
        }

        public static PromptComputation TranslateComputation(
            string outerInput,
            IReadOnlyList<PromptHighlightSpan> outerHighlights,
            PromptInterpretationState outerInterpretationState,
            NestedPromptResolution resolution,
            PromptComputation nestedComputation) {
            var offset = resolution.OuterEditTarget.StartIndex;
            var outerStart = resolution.OuterEditTarget.StartIndex;
            var outerEnd = resolution.OuterEditTarget.EndIndex;

            var translatedSuggestions = nestedComputation.Suggestions.Select(suggestion => new PromptCompletionItem {
                Id = suggestion.Id,
                DisplayText = suggestion.DisplayText,
                SecondaryDisplayText = suggestion.SecondaryDisplayText,
                TrailingDisplayText = suggestion.TrailingDisplayText,
                SummaryText = suggestion.SummaryText,
                DisplayStyleId = suggestion.DisplayStyleId,
                SecondaryDisplayStyleId = suggestion.SecondaryDisplayStyleId,
                TrailingDisplayStyleId = suggestion.TrailingDisplayStyleId,
                SummaryStyleId = suggestion.SummaryStyleId,
                PreviewStyleId = suggestion.PreviewStyleId,
                DisplayHighlights = suggestion.DisplayHighlights,
                SecondaryDisplayHighlights = suggestion.SecondaryDisplayHighlights,
                TrailingDisplayHighlights = suggestion.TrailingDisplayHighlights,
                SummaryHighlights = suggestion.SummaryHighlights,
                Weight = suggestion.Weight,
                PrimaryEdit = new PromptTextEdit(
                    offset + suggestion.PrimaryEdit.StartIndex,
                    suggestion.PrimaryEdit.Length,
                    suggestion.PrimaryEdit.NewText),
            }).ToArray();
            var mergedHighlights = outerHighlights
                .Where(span => span.EndIndex <= outerStart || span.StartIndex >= outerEnd)
                .Concat(nestedComputation.InputHighlights.Select(span => new PromptHighlightSpan(
                    offset + span.StartIndex,
                    span.Length,
                    span.StyleId)))
                .OrderBy(static span => span.StartIndex)
                .ThenBy(static span => span.Length)
                .ToArray();
            var preferredCompletionText = string.IsNullOrWhiteSpace(nestedComputation.PreferredCompletionText)
                ? string.Empty
                : new PromptTextEdit(
                    resolution.OuterEditTarget.StartIndex,
                    resolution.OuterEditTarget.Length,
                    nestedComputation.PreferredCompletionText)
                    .Apply(outerInput ?? string.Empty);
            // Keep the outer interpretation selector visible while the outer route is still ambiguous.
            // Nested command-ref prompts can supply richer candidates/status for the active slot,
            // but replacing the visible interpretation state too early discards the operator's outer
            // route choice before the raw input has committed that decision.
            var interpretationState = outerInterpretationState?.Interpretations.Length > 1
                ? outerInterpretationState
                : nestedComputation.InterpretationState;

            return new PromptComputation(
                Suggestions: translatedSuggestions,
                StatusBodyLines: nestedComputation.StatusBodyLines,
                InputHighlights: mergedHighlights,
                PreferredCompletionText: preferredCompletionText,
                InterpretationState: interpretationState);
        }

        private static PromptInputState CreateNestedState(
            PromptInputState outerState,
            PromptInterpretationState outerInterpretationState,
            PromptEditTarget outerEditTarget) {
            var rawText = outerEditTarget.RawText ?? string.Empty;
            return new PromptInputState {
                InputText = rawText,
                CursorIndex = Math.Clamp(outerState.CursorIndex - outerEditTarget.StartIndex, 0, rawText.Length),
                CompletionIndex = outerState.CompletionIndex,
                CompletionCount = outerState.CompletionCount,
                CandidateWindowOffset = outerState.CandidateWindowOffset,
                PreferredInterpretationId = HasVisibleInterpretationChooser(outerInterpretationState)
                    ? string.Empty
                    : outerState.PreferredInterpretationId,
            };
        }

        private static bool HasVisibleInterpretationChooser(PromptInterpretationState interpretationState) {
            return interpretationState?.Interpretations.Length > 1;
        }

        private static string ResolveDisplayText(PromptAlternativeSpec alternative) {
            return string.IsNullOrWhiteSpace(alternative.ResultDisplayText)
                ? alternative.Title ?? string.Empty
                : alternative.ResultDisplayText;
        }
    }
}
