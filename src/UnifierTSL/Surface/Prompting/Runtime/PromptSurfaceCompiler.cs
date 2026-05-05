using System.Collections.Immutable;
using UnifierTSL.Surface.Prompting.Sessions;
using UnifierTSL.Surface.Prompting.Semantics;
using UnifierTSL.Surface.Prompting;
using UnifierTSL.Commanding;
using UnifierTSL.Contracts.Projection;
using UnifierTSL.Surface.Prompting.Model;

namespace UnifierTSL.Surface.Prompting.Runtime
{
    internal readonly record struct PromptRuntimeRevision(
        long RuntimeResolverRevision,
        long ParameterExplainerRevision,
        long ParameterCandidateProviderRevision);

    internal readonly record struct PromptCompilationResult(
        ProjectionDocumentContent Content,
        PromptComputation Computation);

    internal sealed class PromptSurfaceCompiler(
        PromptSurfaceSpec promptSpec,
        PromptSurfaceScenario initialScenario = PromptSurfaceScenario.PagedInitial,
        PromptSurfaceScenario reactiveScenario = PromptSurfaceScenario.PagedReactive)
    {
        private readonly PromptSurfaceSpec promptSpec = promptSpec ?? throw new ArgumentNullException(nameof(promptSpec));
        private readonly PromptEngine promptEngine = new();
        public PromptInputPurpose Purpose => promptSpec.Purpose;

        public PromptCompilationResult BuildInitial() {
            var initialState = PromptInteractionRunner.CreateInitialInputState();
            return BuildCompilation(initialState, initialScenario);
        }

        public PromptCompilationResult BuildReactive(PromptInputState inputState) {
            NormalizeInputState(inputState);
            return BuildCompilation(inputState, reactiveScenario);
        }

        public PromptRuntimeRevision GetRuntimeRevision(PromptInputPurpose purpose, PromptInputState inputState) {

            NormalizeInputState(inputState);
            PromptResolveContext resolveContext = new(
                Purpose: purpose,
                State: inputState,
                Scenario: reactiveScenario);

            long runtimeResolverRevision = 0;
            if (promptSpec.RuntimeResolver is not null) {
                try {
                    runtimeResolverRevision = promptSpec.RuntimeResolver.GetRevision(resolveContext);
                }
                catch {
                }
            }

            var parameterExplainerRevision = GetExplainerRevision(inputState);
            var parameterCandidateProviderRevision = GetCandidateProviderRevision(inputState);
            return new(runtimeResolverRevision, parameterExplainerRevision, parameterCandidateProviderRevision);
        }

        private PromptCompilationResult BuildCompilation(PromptInputState inputState, PromptSurfaceScenario scenario) {
            var authoredContent = ResolveContent(inputState, scenario);
            var resolvedPrompt = CreateResolvedPrompt(promptSpec, authoredContent);
            var computed = promptEngine.Compute(
                resolvedPrompt,
                inputState,
                scenario,
                ReadStatusBodyLines(authoredContent));
            inputState.PreferredInterpretationId = ResolvePreferredInterpretationId(
                inputState.PreferredInterpretationId,
                computed.InterpretationState);

            return new PromptCompilationResult(
                authoredContent,
                computed);
        }

        private ProjectionDocumentContent ResolveContent(PromptInputState inputState, PromptSurfaceScenario scenario) {
            var content = promptSpec.Content;
            if (promptSpec.RuntimeResolver is null) {
                return content;
            }

            try {
                PromptResolveContext resolveContext = new(
                    Purpose: promptSpec.Purpose,
                    State: inputState,
                    Scenario: scenario);
                return promptSpec.RuntimeResolver.Resolve(resolveContext);
            }
            catch {
                return content;
            }
        }

        private static ResolvedPrompt CreateResolvedPrompt(
            PromptSurfaceSpec promptSpec,
            ProjectionDocumentContent content) {
            var completion = PromptProjectionDocumentFactory.FindState<CollectionProjectionNodeState>(
                content,
                PromptProjectionDocumentFactory.NodeIds.CompletionOptions,
                EditorProjectionSemanticKeys.AssistPrimaryList);
            var candidates = NormalizeCandidateMap(promptSpec.StaticCandidates);
            var plainCandidates = ReadPlainCandidates(completion?.State.Items);
            if (plainCandidates.Length > 0) {
                candidates = candidates.SetItem(
                    string.Empty,
                    PromptSuggestionOps.Normalize(plainCandidates));
            }

            var parameterExplainers = NormalizeParameterExplainers(promptSpec.ParameterExplainers);
            var parameterCandidateProviders = NormalizeParameterCandidateProviders(promptSpec.ParameterCandidateProviders);

            return new ResolvedPrompt {
                Purpose = promptSpec.Purpose,
                Server = promptSpec.Server,
                SemanticSpec = promptSpec.SemanticSpec,
                Candidates = candidates,
                ParameterExplainers = parameterExplainers,
                ParameterCandidateProviders = parameterCandidateProviders,
            };
        }

        private long GetExplainerRevision(PromptInputState inputState) {
            var revisionContext = CreateResolvedPrompt(promptSpec, promptSpec.Content);
            if (!promptEngine.TryCreateExplainContext(
                revisionContext,
                inputState,
                reactiveScenario,
                out var explainContext)) {
                return 0;
            }

            if (explainContext.ActiveSlot.SemanticKey is not SemanticKey semanticKey) {
                return 0;
            }

            var explainer = revisionContext.ResolveParameterExplainer(semanticKey);
            if (explainer is null) {
                return 0;
            }

            try {
                return explainer.GetRevision(explainContext);
            }
            catch {
                return 0;
            }
        }

        private long GetCandidateProviderRevision(PromptInputState inputState) {
            var revisionContext = CreateResolvedPrompt(promptSpec, promptSpec.Content);
            if (!promptEngine.TryCreateCandidateContext(
                revisionContext,
                inputState,
                reactiveScenario,
                out var candidateContext)) {
                return 0;
            }

            if (candidateContext.ActiveSlot.SemanticKey is not SemanticKey semanticKey) {
                return 0;
            }

            var provider = revisionContext.ResolveParameterCandidateProvider(semanticKey);
            if (provider is null) {
                return 0;
            }

            try {
                return provider.GetRevision(candidateContext);
            }
            catch {
                return 0;
            }
        }

        private static void NormalizeInputState(PromptInputState inputState) {
            inputState.Normalize(
                clampCursorToText: false,
                trimPreferredInterpretationId: true,
                normalizePreferredCompletionText: false);
        }

        private static string ResolvePreferredInterpretationId(string currentPreference, PromptInterpretationState interpretationState) {
            if (interpretationState.Interpretations.Length <= 1 || string.IsNullOrWhiteSpace(currentPreference)) {
                return string.Empty;
            }

            return interpretationState.Interpretations.Any(interpretation => interpretation.Id.Equals(currentPreference, StringComparison.Ordinal))
                ? currentPreference
                : string.Empty;
        }

        private static ImmutableDictionary<string, ImmutableArray<PromptSuggestion>> NormalizeCandidateMap(
            ImmutableDictionary<string, ImmutableArray<PromptSuggestion>> source) {

            if (source.Count == 0) {
                return ImmutableDictionary<string, ImmutableArray<PromptSuggestion>>.Empty;
            }

            var builder = ImmutableDictionary.CreateBuilder<string, ImmutableArray<PromptSuggestion>>();
            foreach ((var key, var suggestions) in source) {
                builder[key?.Trim() ?? string.Empty] = PromptSuggestionOps.Normalize(suggestions);
            }

            return builder.ToImmutable();
        }

        private static ImmutableDictionary<SemanticKey, IParamValueExplainer> NormalizeParameterExplainers(
            ImmutableDictionary<SemanticKey, IParamValueExplainer> source) {

            if (source.Count == 0) {
                return ImmutableDictionary<SemanticKey, IParamValueExplainer>.Empty;
            }

            var builder = ImmutableDictionary.CreateBuilder<SemanticKey, IParamValueExplainer>();
            foreach ((var key, var explainer) in source) {
                if (explainer is not null) {
                    builder[key] = explainer;
                }
            }

            return builder.ToImmutable();
        }

        private static ImmutableDictionary<SemanticKey, IParamValueCandidateProvider> NormalizeParameterCandidateProviders(
            ImmutableDictionary<SemanticKey, IParamValueCandidateProvider> source) {

            if (source.Count == 0) {
                return ImmutableDictionary<SemanticKey, IParamValueCandidateProvider>.Empty;
            }

            var builder = ImmutableDictionary.CreateBuilder<SemanticKey, IParamValueCandidateProvider>();
            foreach ((var key, var provider) in source) {
                if (provider is not null) {
                    builder[key] = provider;
                }
            }

            return builder.ToImmutable();
        }

        private static ImmutableArray<string> ReadStatusBodyLines(ProjectionDocumentContent content) {
            var detail = PromptProjectionDocumentFactory.FindState<DetailProjectionNodeState>(
                content,
                PromptProjectionDocumentFactory.NodeIds.StatusDetail,
                EditorProjectionSemanticKeys.StatusDetail);
            return [.. (detail?.State.Lines ?? [])
                .Select(PromptProjectionDocumentFactory.ReadText)
                .Where(static line => !string.IsNullOrWhiteSpace(line))
                .Distinct(StringComparer.OrdinalIgnoreCase)];
        }

        private static ImmutableArray<PromptSuggestion> ReadPlainCandidates(IReadOnlyList<ProjectionCollectionItem>? items) {
            return items is not { Count: > 0 }
                ? []
                : [.. items
                    .Select(ReadSuggestion)
                    .Where(static suggestion => !string.IsNullOrWhiteSpace(suggestion.Value))];
        }

        private static PromptSuggestion ReadSuggestion(ProjectionCollectionItem item) {
            var value = item.PrimaryEdit is { TargetNodeId: not null } edit
                && string.Equals(edit.TargetNodeId, PromptProjectionDocumentFactory.NodeIds.Input, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(edit.NewText)
                    ? edit.NewText
                    : PromptProjectionDocumentFactory.ReadText(item.Label);
            return new PromptSuggestion(value);
        }
    }
}
