using System.Collections.Immutable;
using UnifierTSL.ConsoleClient.Shell;

namespace UnifierTSL.CLI.Sessions
{
    internal sealed class DefaultReadLineContextMaterializer : IReadLineContextMaterializer
    {
        private readonly ReadLineSemanticEngine semanticEngine = new();

        public ReadLineReactiveState CreateInitialReactiveState(ReadLineContextSpec contextSpec)
        {
            return new ReadLineReactiveState {
                Purpose = contextSpec.Purpose,
                InputText = string.Empty,
                CursorIndex = 0,
                CompletionIndex = 0,
                CompletionCount = 0,
                CandidateWindowOffset = 0,
            };
        }

        public ReadLineResolvedContext BuildContext(
            ReadLineContextSpec contextSpec,
            ReadLineReactiveState state,
            ReadLineMaterializationScenario scenario)
        {
            ArgumentNullException.ThrowIfNull(contextSpec);
            ArgumentNullException.ThrowIfNull(state);

            ImmutableDictionary<ReadLineTargetKey, ImmutableArray<ReadLineSuggestion>> candidates = NormalizeCandidateMap(contextSpec.StaticCandidates);
            ImmutableArray<string> statusLines = NormalizeStatusLines(contextSpec.BaseStatusLines);
            string ghostText = contextSpec.GhostText ?? string.Empty;
            string helpText = contextSpec.HelpText ?? string.Empty;
            string parameterHint = contextSpec.ParameterHint ?? string.Empty;

            if (contextSpec.DynamicResolver is not null) {
                try {
                    ReadLineResolveContext resolveContext = new(
                        State: CloneState(state),
                        Scenario: scenario);
                    ReadLineDynamicPatch? patch = contextSpec.DynamicResolver(resolveContext);
                    if (patch is not null) {
                        if (patch.CandidateOverrides.Count > 0) {
                            ImmutableDictionary<ReadLineTargetKey, ImmutableArray<ReadLineSuggestion>>.Builder mapBuilder = candidates.ToBuilder();
                            foreach ((ReadLineTargetKey key, ImmutableArray<ReadLineSuggestion> value) in patch.CandidateOverrides) {
                                mapBuilder[key] = NormalizeSuggestions(value);
                            }
                            candidates = mapBuilder.ToImmutable();
                        }

                        if (patch.AdditionalStatusLines.Length > 0) {
                            statusLines = NormalizeStatusLines(statusLines.AddRange(patch.AdditionalStatusLines));
                        }

                        ghostText = patch.GhostTextOverride ?? ghostText;
                        helpText = patch.HelpTextOverride ?? helpText;
                        parameterHint = patch.ParameterHintOverride ?? parameterHint;
                    }
                }
                catch {
                }
            }

            if (!candidates.ContainsKey(ReadLineTargetKeys.Boolean)) {
                candidates = candidates.Add(ReadLineTargetKeys.Boolean, ReadLineTargetKeys.DefaultBooleanSuggestions);
            }

            ReadLineResolvedContext context = new() {
                Purpose = contextSpec.Purpose,
                Prompt = contextSpec.Prompt ?? "> ",
                GhostText = ghostText,
                EmptySubmitBehavior = contextSpec.EmptySubmitBehavior,
                EnableCtrlEnterBypassGhostFallback = contextSpec.EnableCtrlEnterBypassGhostFallback,
                HelpText = helpText,
                ParameterHint = parameterHint,
                StatusPanelHeight = contextSpec.StatusPanelHeight,
                StatusLines = statusLines,
                AllowAnsiStatusEscapes = contextSpec.AllowAnsiStatusEscapes,
                UsePrecomputedCandidates = contextSpec.UsePrecomputedCandidates,
                SkipDerivedStatus = contextSpec.SkipDerivedStatus,
                CommandPrefixes = NormalizePrefixes(contextSpec.CommandPrefixes),
                CommandHints = NormalizeCommandHints(contextSpec.CommandHints),
                Candidates = candidates,
            };

            return ApplyScenarioFiltering(context, state, scenario);
        }

        private ReadLineResolvedContext ApplyScenarioFiltering(
            ReadLineResolvedContext source,
            ReadLineReactiveState state,
            ReadLineMaterializationScenario scenario)
        {
            ReadLineResolvedContext context = source;

            switch (scenario) {
                case ReadLineMaterializationScenario.ProtocolInitial:
                case ReadLineMaterializationScenario.ProtocolReactive:
                    if (context.Purpose == ConsoleInputPurpose.CommandLine) {
                        context = ApplyCommandLineProtocolCompaction(context, state);
                    }
                    break;

                case ReadLineMaterializationScenario.NonInteractiveFallback:
                    context = context with {
                        StatusPanelHeight = Math.Clamp(context.StatusPanelHeight, 1, 4),
                    };
                    break;
            }

            return context;
        }

        private ReadLineResolvedContext ApplyCommandLineProtocolCompaction(ReadLineResolvedContext context, ReadLineReactiveState state)
        {
            ReadLineCommandParse parse = semanticEngine.ParseCommandInput(state.InputText, context);

            HashSet<ReadLineTargetKey> trimTargets = [
                ReadLineTargetKeys.Plain,
                ReadLineTargetKeys.Player,
                ReadLineTargetKeys.Server,
                ReadLineTargetKeys.Item,
                ReadLineTargetKeys.Boolean,
                ReadLineTargetKeys.Enum,
            ];

            if (parse.IsCommandToken || parse.ArgumentIndex < 0) {
                return context with {
                    Candidates = context.Candidates.RemoveRange(trimTargets),
                    HelpText = string.Empty,
                };
            }

            ReadLineTargetKey target = semanticEngine.ResolveParameterTarget(parse, context);
            ImmutableDictionary<ReadLineTargetKey, ImmutableArray<ReadLineSuggestion>>.Builder builder = context.Candidates.ToBuilder();
            foreach (ReadLineTargetKey key in trimTargets) {
                if (key == target) {
                    continue;
                }

                builder.Remove(key);
            }

            return context with {
                Candidates = builder.ToImmutable(),
            };
        }

        private static ReadLineReactiveState CloneState(ReadLineReactiveState source)
        {
            return new ReadLineReactiveState {
                Purpose = source.Purpose,
                InputText = source.InputText,
                CursorIndex = source.CursorIndex,
                CompletionIndex = source.CompletionIndex,
                CompletionCount = source.CompletionCount,
                CandidateWindowOffset = source.CandidateWindowOffset,
            };
        }

        private static ImmutableDictionary<ReadLineTargetKey, ImmutableArray<ReadLineSuggestion>> NormalizeCandidateMap(
            ImmutableDictionary<ReadLineTargetKey, ImmutableArray<ReadLineSuggestion>> source)
        {
            if (source.Count == 0) {
                return ImmutableDictionary<ReadLineTargetKey, ImmutableArray<ReadLineSuggestion>>.Empty;
            }

            ImmutableDictionary<ReadLineTargetKey, ImmutableArray<ReadLineSuggestion>>.Builder builder =
                ImmutableDictionary.CreateBuilder<ReadLineTargetKey, ImmutableArray<ReadLineSuggestion>>();

            foreach ((ReadLineTargetKey key, ImmutableArray<ReadLineSuggestion> suggestions) in source) {
                builder[key] = NormalizeSuggestions(suggestions);
            }

            return builder.ToImmutable();
        }

        private static ImmutableArray<ReadLineSuggestion> NormalizeSuggestions(IEnumerable<ReadLineSuggestion> source)
        {
            return [.. source
                .Where(static item => !string.IsNullOrWhiteSpace(item.Value))
                .GroupBy(static item => item.Value, StringComparer.OrdinalIgnoreCase)
                .Select(static group => group.OrderByDescending(static item => item.Weight).First())
                .OrderByDescending(static item => item.Weight)
                .ThenBy(static item => item.Value, StringComparer.OrdinalIgnoreCase)];
        }

        private static ImmutableArray<ConsoleCommandHint> NormalizeCommandHints(IEnumerable<ConsoleCommandHint> hints)
        {
            return [.. hints
                .Where(static hint => hint is not null && !string.IsNullOrWhiteSpace(hint.PrimaryName))
                .GroupBy(static hint => hint.PrimaryName, StringComparer.OrdinalIgnoreCase)
                .Select(static group => group.First())];
        }

        private static ImmutableArray<string> NormalizePrefixes(IEnumerable<string> prefixes)
        {
            ImmutableArray<string> normalized = [.. prefixes
                .Where(static prefix => !string.IsNullOrWhiteSpace(prefix))
                .Select(static prefix => prefix.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)];

            return normalized.Length == 0 ? ["/"] : normalized;
        }

        private static ImmutableArray<string> NormalizeStatusLines(IEnumerable<string> lines)
        {
            return [.. lines
                .Where(static line => !string.IsNullOrWhiteSpace(line))
                .Distinct(StringComparer.OrdinalIgnoreCase)];
        }
    }
}
