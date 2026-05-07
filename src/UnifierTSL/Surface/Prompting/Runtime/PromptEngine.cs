using UnifierTSL.Surface.Prompting.Semantics;
using UnifierTSL.Surface.Prompting.Model;

namespace UnifierTSL.Surface.Prompting.Runtime
{
    internal readonly record struct PromptComputation(
        IReadOnlyList<PromptCompletionItem> Suggestions,
        IReadOnlyList<string> StatusBodyLines,
        IReadOnlyList<PromptHighlightSpan> InputHighlights,
        string PreferredCompletionText,
        PromptInterpretationState InterpretationState);

    internal sealed class PromptEngine
    {
        public PromptComputation Compute(
            ResolvedPrompt context,
            PromptInputState state,
            PromptSurfaceScenario scenario,
            IReadOnlyList<string> baseStatusBodyLines) {

            var semanticAnalysis = PromptSemanticAnalyzer.TryAnalyze(context, state, scenario);
            if (semanticAnalysis is null) {
                var plainSuggestions = PromptSemanticCompletion.ResolvePlainSuggestions(state.InputText, context);
                return new PromptComputation(
                    Suggestions: plainSuggestions,
                    StatusBodyLines: baseStatusBodyLines,
                    InputHighlights: [],
                    PreferredCompletionText: string.Empty,
                    InterpretationState: PromptInterpretationState.Empty);
            }

            var analysis = semanticAnalysis.Value;
            var hasNestedPrompt = PromptNestedPromptResolver.TryResolve(
                context,
                analysis,
                state,
                scenario,
                out var nestedResolution);

            var suggestions = PromptSemanticCompletion.ResolveSemanticSuggestions(
                context,
                analysis,
                state,
                scenario);
            var presentationAnalysis = PromptSemanticCompletion.ResolvePresentationAnalysis(
                context,
                analysis,
                suggestions,
                state,
                scenario);
            var interpretationState = PromptSemanticCompletion.BuildInterpretationState(
                context,
                analysis,
                presentationAnalysis,
                state,
                scenario,
                baseStatusBodyLines,
                out var statusBodyLines);
            if (hasNestedPrompt) {
                var nestedComputation = Compute(
                    nestedResolution.Context,
                    nestedResolution.State,
                    scenario,
                    baseStatusBodyLines);
                return PromptNestedPromptResolver.TranslateComputation(
                    state.InputText ?? string.Empty,
                    analysis.HighlightSpans,
                    interpretationState,
                    nestedResolution,
                    nestedComputation);
            }

            var preferredCompletionText = PromptSemanticCompletion.ResolvePresentationInputText(
                state.InputText ?? string.Empty,
                suggestions,
                state);
            return new PromptComputation(
                Suggestions: suggestions,
                StatusBodyLines: statusBodyLines,
                InputHighlights: analysis.HighlightSpans,
                PreferredCompletionText: string.Equals(preferredCompletionText, state.InputText ?? string.Empty, StringComparison.Ordinal)
                    ? string.Empty
                    : preferredCompletionText,
                InterpretationState: interpretationState);
        }

        public bool TryCreateExplainContext(
            ResolvedPrompt context,
            PromptInputState state,
            PromptSurfaceScenario scenario,
            out PromptParamExplainContext explainContext) {

            explainContext = default;
            if (PromptSemanticAnalyzer.TryAnalyze(context, state, scenario) is not PromptSemanticAnalysis analysis
                || !PromptSemanticCompletion.TryCreateExplainContext(context, analysis, state, scenario, out explainContext)) {
                return false;
            }

            return true;
        }

        public bool TryCreateCandidateContext(
            ResolvedPrompt context,
            PromptInputState state,
            PromptSurfaceScenario scenario,
            out PromptParamCandidateContext candidateContext) {

            candidateContext = default;
            if (PromptSemanticAnalyzer.TryAnalyze(context, state, scenario) is not PromptSemanticAnalysis analysis
                || !PromptSemanticCompletion.TryCreateCandidateContext(context, analysis, state, scenario, out candidateContext)) {
                return false;
            }

            return true;
        }
    }
}
