using System.Collections.Immutable;
using UnifierTSL.ConsoleClient.Shared.ConsolePrompting;

namespace UnifierTSL.CLI.Prompting
{
    internal readonly record struct ConsolePromptRuntimeRevision(
        long RuntimeResolverRevision,
        long ParameterExplainerRevision);

    internal sealed class ConsolePromptCompiler(
        ConsolePromptSpec promptSpec,
        ConsolePromptScenario initialScenario = ConsolePromptScenario.PagedInitial,
        ConsolePromptScenario reactiveScenario = ConsolePromptScenario.PagedReactive)
    {
        private readonly ConsolePromptSpec promptSpec = promptSpec ?? throw new ArgumentNullException(nameof(promptSpec));
        private readonly ConsolePromptEngine promptEngine = new();
        private readonly ConsoleResolvedPrompt runtimeRevisionProbeContext = BuildRuntimeRevisionProbeContext(promptSpec);

        public ConsolePromptSnapshot BuildInitial() {
            ConsoleInputState initialState = ConsolePromptSessionRunner.CreateInitialInputState(promptSpec.Purpose);
            return BuildSnapshot(initialState, initialScenario);
        }

        public ConsolePromptSnapshot BuildReactive(ConsoleInputState inputState) {
            ArgumentNullException.ThrowIfNull(inputState);
            return BuildSnapshot(NormalizeInputState(inputState), reactiveScenario);
        }

        public ConsolePromptRuntimeRevision GetRuntimeRevision(ConsoleInputState inputState) {
            ArgumentNullException.ThrowIfNull(inputState);

            ConsoleInputState normalizedState = NormalizeInputState(inputState);
            ConsolePromptResolveContext resolveContext = new(
                State: normalizedState,
                Scenario: reactiveScenario);
            long runtimeResolverRevision = 0;
            if (promptSpec.RuntimeResolver is not null) {
                try {
                    runtimeResolverRevision = promptSpec.RuntimeResolver.GetRevision(resolveContext);
                }
                catch {
                }
            }

            long parameterExplainerRevision = ResolveParameterExplainerRevision(normalizedState);
            return new(runtimeResolverRevision, parameterExplainerRevision);
        }

        private ConsolePromptSnapshot BuildSnapshot(ConsoleInputState inputState, ConsolePromptScenario scenario) {
            ConsoleResolvedPrompt resolvedPrompt = ResolvePrompt(inputState, scenario);
            PromptComputation computed = promptEngine.Compute(resolvedPrompt, inputState, scenario);

            return new ConsolePromptSnapshot {
                Purpose = resolvedPrompt.Purpose,
                Prompt = resolvedPrompt.Prompt,
                CommandPrefixes = [.. resolvedPrompt.CommandPrefixes],
                GhostText = resolvedPrompt.GhostText,
                EmptySubmitBehavior = resolvedPrompt.EmptySubmitBehavior,
                EnableCtrlEnterBypassGhostFallback = resolvedPrompt.EnableCtrlEnterBypassGhostFallback,
                InputSummary = resolvedPrompt.InputSummary,
                StatusBodyLines = [.. computed.StatusBodyLines],
                Theme = resolvedPrompt.Theme with { },
                Candidates = [.. computed.Suggestions.Select(static value => new ConsoleSuggestionEntry {
                    Value = value,
                })],
            };
        }

        private ConsoleResolvedPrompt ResolvePrompt(ConsoleInputState inputState, ConsolePromptScenario scenario) {
            ImmutableDictionary<ConsoleSuggestionKind, ImmutableArray<ConsoleSuggestion>> candidates =
                NormalizeCandidateMap(promptSpec.StaticCandidates);
            ImmutableDictionary<string, IConsoleParameterValueExplainer> parameterExplainers =
                NormalizeParameterExplainers(promptSpec.ParameterExplainers);
            ImmutableArray<string> statusBodyLines = NormalizeStatusBodyLines(promptSpec.BaseStatusBodyLines);
            string inputSummary = promptSpec.InputSummary ?? string.Empty;
            string ghostText = promptSpec.GhostText ?? string.Empty;
            string helpText = promptSpec.HelpText ?? string.Empty;
            ConsolePromptTheme theme = promptSpec.Theme ?? ConsolePromptTheme.Default;

            if (promptSpec.RuntimeResolver is not null) {
                try {
                    ConsolePromptResolveContext resolveContext = new(
                        State: inputState,
                        Scenario: scenario);
                    ConsolePromptUpdate? patch = promptSpec.RuntimeResolver.Resolve(resolveContext);
                    if (patch is not null) {
                        if (patch.CandidateOverrides.Count > 0) {
                            ImmutableDictionary<ConsoleSuggestionKind, ImmutableArray<ConsoleSuggestion>>.Builder mapBuilder = candidates.ToBuilder();
                            foreach ((ConsoleSuggestionKind key, ImmutableArray<ConsoleSuggestion> value) in patch.CandidateOverrides) {
                                mapBuilder[key] = ConsoleSuggestionOps.Normalize(value);
                            }
                            candidates = mapBuilder.ToImmutable();
                        }

                        if (patch.AdditionalStatusBodyLines.Length > 0) {
                            statusBodyLines = NormalizeStatusBodyLines(statusBodyLines.AddRange(patch.AdditionalStatusBodyLines));
                        }

                        inputSummary = patch.InputSummaryOverride ?? inputSummary;
                        theme = patch.ThemeOverride ?? theme;
                    }
                }
                catch {
                }
            }

            if (!candidates.ContainsKey(ConsoleSuggestionKind.Boolean)) {
                candidates = candidates.Add(ConsoleSuggestionKind.Boolean, ConsoleSuggestionCatalog.DefaultBooleanSuggestions);
            }

            ConsoleResolvedPrompt resolvedPrompt = new() {
                Purpose = promptSpec.Purpose,
                Server = promptSpec.Server,
                Prompt = promptSpec.Prompt ?? "> ",
                GhostText = ghostText,
                EmptySubmitBehavior = promptSpec.EmptySubmitBehavior,
                EnableCtrlEnterBypassGhostFallback = promptSpec.EnableCtrlEnterBypassGhostFallback,
                HelpText = helpText,
                InputSummary = inputSummary,
                StatusBodyLines = statusBodyLines,
                Theme = theme with { },
                CommandPrefixes = NormalizePrefixes(promptSpec.CommandPrefixes),
                CommandSpecs = NormalizeCommandSpecs(promptSpec.CommandSpecs),
                Candidates = candidates,
                ParameterExplainers = parameterExplainers,
            };

            return ApplyScenarioFiltering(resolvedPrompt, inputState, scenario);
        }

        private ConsoleResolvedPrompt ApplyScenarioFiltering(
            ConsoleResolvedPrompt source,
            ConsoleInputState inputState,
            ConsolePromptScenario scenario) {
            return scenario switch {
                ConsolePromptScenario.PagedInitial or ConsolePromptScenario.PagedReactive
                    when source.Purpose == ConsoleInputPurpose.CommandLine
                    => ApplyPagedCommandLineCompaction(source, inputState),
                _ => source,
            };
        }

        private long ResolveParameterExplainerRevision(ConsoleInputState inputState) {
            if (!promptEngine.TryCreateParameterExplainContext(
                runtimeRevisionProbeContext,
                inputState,
                reactiveScenario,
                out ConsoleParameterExplainContext explainContext)) {
                return 0;
            }

            IConsoleParameterValueExplainer? explainer =
                runtimeRevisionProbeContext.ResolveParameterExplainer(explainContext.ActiveParameter.SemanticKey!);
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

        private ConsoleResolvedPrompt ApplyPagedCommandLineCompaction(
            ConsoleResolvedPrompt resolvedPrompt,
            ConsoleInputState inputState) {
            CommandParseResult parse = promptEngine.ParseCommandInput(inputState.InputText, resolvedPrompt);

            HashSet<ConsoleSuggestionKind> trimTargets = [
                ConsoleSuggestionKind.Plain,
                ConsoleSuggestionKind.Player,
                ConsoleSuggestionKind.Server,
                ConsoleSuggestionKind.Item,
                ConsoleSuggestionKind.Boolean,
                ConsoleSuggestionKind.Enum,
            ];

            if (parse.IsCommandToken || parse.ArgumentIndex < 0) {
                return resolvedPrompt with {
                    Candidates = resolvedPrompt.Candidates.RemoveRange(trimTargets),
                    HelpText = string.Empty,
                };
            }

            ConsoleSuggestionKind target = promptEngine.ResolveParameterKind(parse, resolvedPrompt);
            ImmutableDictionary<ConsoleSuggestionKind, ImmutableArray<ConsoleSuggestion>>.Builder builder =
                resolvedPrompt.Candidates.ToBuilder();
            foreach (ConsoleSuggestionKind key in trimTargets) {
                if (key == target) {
                    continue;
                }

                builder.Remove(key);
            }

            return resolvedPrompt with {
                Candidates = builder.ToImmutable(),
            };
        }

        private static ConsoleInputState NormalizeInputState(ConsoleInputState inputState) {
            return inputState with {
                InputText = inputState.InputText ?? string.Empty,
                CursorIndex = Math.Max(0, inputState.CursorIndex),
                CompletionIndex = Math.Max(0, inputState.CompletionIndex),
                CompletionCount = Math.Max(0, inputState.CompletionCount),
                CandidateWindowOffset = Math.Max(0, inputState.CandidateWindowOffset),
            };
        }

        private static ImmutableDictionary<ConsoleSuggestionKind, ImmutableArray<ConsoleSuggestion>> NormalizeCandidateMap(
            ImmutableDictionary<ConsoleSuggestionKind, ImmutableArray<ConsoleSuggestion>> source) {
            if (source.Count == 0) {
                return ImmutableDictionary<ConsoleSuggestionKind, ImmutableArray<ConsoleSuggestion>>.Empty;
            }

            ImmutableDictionary<ConsoleSuggestionKind, ImmutableArray<ConsoleSuggestion>>.Builder builder =
                ImmutableDictionary.CreateBuilder<ConsoleSuggestionKind, ImmutableArray<ConsoleSuggestion>>();

            foreach ((ConsoleSuggestionKind key, ImmutableArray<ConsoleSuggestion> suggestions) in source) {
                builder[key] = ConsoleSuggestionOps.Normalize(suggestions);
            }

            return builder.ToImmutable();
        }

        private static ImmutableDictionary<string, IConsoleParameterValueExplainer> NormalizeParameterExplainers(
            ImmutableDictionary<string, IConsoleParameterValueExplainer> source) {
            if (source.Count == 0) {
                return ImmutableDictionary<string, IConsoleParameterValueExplainer>.Empty.WithComparers(StringComparer.Ordinal);
            }

            ImmutableDictionary<string, IConsoleParameterValueExplainer>.Builder builder =
                ImmutableDictionary.CreateBuilder<string, IConsoleParameterValueExplainer>(StringComparer.Ordinal);

            foreach ((string key, IConsoleParameterValueExplainer explainer) in source) {
                if (string.IsNullOrWhiteSpace(key) || explainer is null) {
                    continue;
                }

                builder[key.Trim()] = explainer;
            }

            return builder.ToImmutable();
        }

        private static ImmutableArray<ConsoleCommandSpec> NormalizeCommandSpecs(IEnumerable<ConsoleCommandSpec> specs) {
            return [.. specs
                .Where(static spec => spec is not null && !string.IsNullOrWhiteSpace(spec.PrimaryName))
                .GroupBy(static spec => spec.PrimaryName, StringComparer.OrdinalIgnoreCase)
                .Select(static group => group.First())];
        }

        private static ImmutableArray<string> NormalizePrefixes(IEnumerable<string> prefixes) {
            ImmutableArray<string> normalized = [.. prefixes
                .Where(static prefix => !string.IsNullOrWhiteSpace(prefix))
                .Select(static prefix => prefix.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)];

            return normalized.Length == 0 ? ["/"] : normalized;
        }

        private static ImmutableArray<string> NormalizeStatusBodyLines(IEnumerable<string> lines) {
            return [.. lines
                .Where(static line => !string.IsNullOrWhiteSpace(line))
                .Distinct(StringComparer.OrdinalIgnoreCase)];
        }

        private static ConsoleResolvedPrompt BuildRuntimeRevisionProbeContext(ConsolePromptSpec promptSpec) {
            ImmutableDictionary<ConsoleSuggestionKind, ImmutableArray<ConsoleSuggestion>> candidates =
                NormalizeCandidateMap(promptSpec.StaticCandidates);
            if (!candidates.ContainsKey(ConsoleSuggestionKind.Boolean)) {
                candidates = candidates.Add(ConsoleSuggestionKind.Boolean, ConsoleSuggestionCatalog.DefaultBooleanSuggestions);
            }

            return new ConsoleResolvedPrompt {
                Purpose = promptSpec.Purpose,
                Server = promptSpec.Server,
                Prompt = promptSpec.Prompt ?? "> ",
                GhostText = promptSpec.GhostText ?? string.Empty,
                EmptySubmitBehavior = promptSpec.EmptySubmitBehavior,
                EnableCtrlEnterBypassGhostFallback = promptSpec.EnableCtrlEnterBypassGhostFallback,
                HelpText = promptSpec.HelpText ?? string.Empty,
                InputSummary = promptSpec.InputSummary ?? string.Empty,
                StatusBodyLines = NormalizeStatusBodyLines(promptSpec.BaseStatusBodyLines),
                Theme = (promptSpec.Theme ?? ConsolePromptTheme.Default) with { },
                CommandPrefixes = NormalizePrefixes(promptSpec.CommandPrefixes),
                CommandSpecs = NormalizeCommandSpecs(promptSpec.CommandSpecs),
                Candidates = candidates,
                ParameterExplainers = NormalizeParameterExplainers(promptSpec.ParameterExplainers),
            };
        }
    }
}
