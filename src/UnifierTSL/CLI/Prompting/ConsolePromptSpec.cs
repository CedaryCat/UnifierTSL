using System.Collections.Immutable;
using UnifierTSL.ConsoleClient.Shared.ConsolePrompting;
using UnifierTSL.Servers;

namespace UnifierTSL.CLI.Prompting
{
    public enum ConsolePromptScenario : byte
    {
        LocalInteractive,
        PagedInitial,
        PagedReactive,
        NonInteractiveFallback,
    }

    public enum ConsoleSuggestionKind : byte
    {
        Plain,
        Player,
        Server,
        Item,
        Boolean,
        Enum,
    }

    public static class ConsoleSuggestionCatalog
    {
        public static ImmutableArray<ConsoleSuggestion> DefaultBooleanSuggestions { get; } = [
            new ConsoleSuggestion("true"),
            new ConsoleSuggestion("false"),
            new ConsoleSuggestion("on"),
            new ConsoleSuggestion("off"),
            new ConsoleSuggestion("1"),
            new ConsoleSuggestion("0"),
        ];
    }

    public readonly record struct ConsoleSuggestion(string Value, int Weight = 0);

    public sealed record ConsoleCommandSpec
    {
        public string PrimaryName { get; init; } = string.Empty;

        public ImmutableArray<string> Aliases { get; init; } = [];

        public string HelpText { get; init; } = string.Empty;

        public ImmutableArray<ConsoleCommandPatternSpec> Patterns { get; init; } = [];
    }

    public sealed record ConsoleCommandPatternSpec
    {
        public ImmutableArray<string> SubCommands { get; init; } = [];

        public ImmutableArray<ConsoleCommandParameterSpec> Parameters { get; init; } = [];
    }

    public sealed record ConsoleCommandParameterSpec
    {
        public string Name { get; init; } = string.Empty;

        public ConsoleSuggestionKind Kind { get; init; } = ConsoleSuggestionKind.Plain;

        public string? SemanticKey { get; init; }

        public bool Optional { get; init; }

        public bool Variadic { get; init; }

        public ImmutableArray<string> EnumCandidates { get; init; } = [];
    }

    public readonly record struct ConsolePromptResolveContext(
        ConsoleInputState State,
        ConsolePromptScenario Scenario);

    public interface IConsolePromptRuntimeResolver
    {
        long GetRevision(ConsolePromptResolveContext context);

        ConsolePromptUpdate Resolve(ConsolePromptResolveContext context);
    }

    public static class ConsolePromptRuntimeResolver
    {
        public static IConsolePromptRuntimeResolver Create(
            Func<ConsolePromptResolveContext, ConsolePromptUpdate> resolve,
            Func<ConsolePromptResolveContext, long> getRevision) {
            ArgumentNullException.ThrowIfNull(resolve);
            ArgumentNullException.ThrowIfNull(getRevision);
            return new DelegateConsolePromptRuntimeResolver(resolve, getRevision);
        }

        private sealed class DelegateConsolePromptRuntimeResolver(
            Func<ConsolePromptResolveContext, ConsolePromptUpdate> resolve,
            Func<ConsolePromptResolveContext, long> getRevision) : IConsolePromptRuntimeResolver
        {
            public long GetRevision(ConsolePromptResolveContext context) {
                return getRevision(context);
            }

            public ConsolePromptUpdate Resolve(ConsolePromptResolveContext context) {
                return resolve(context);
            }
        }
    }

    public sealed record ConsolePromptUpdate
    {
        public ImmutableDictionary<ConsoleSuggestionKind, ImmutableArray<ConsoleSuggestion>> CandidateOverrides { get; init; } =
            ImmutableDictionary<ConsoleSuggestionKind, ImmutableArray<ConsoleSuggestion>>.Empty;

        public ImmutableArray<string> AdditionalStatusBodyLines { get; init; } = [];

        public string? InputSummaryOverride { get; init; }

        public ConsolePromptTheme? ThemeOverride { get; init; }
    }

    public sealed record ConsolePromptSpec
    {
        public ConsoleInputPurpose Purpose { get; init; } = ConsoleInputPurpose.Plain;

        public ServerContext? Server { get; init; }

        public string Prompt { get; init; } = "> ";

        public string GhostText { get; init; } = string.Empty;

        public EmptySubmitBehavior EmptySubmitBehavior { get; init; } = EmptySubmitBehavior.KeepInput;

        public bool EnableCtrlEnterBypassGhostFallback { get; init; } = true;

        public string HelpText { get; init; } = string.Empty;

        public string InputSummary { get; init; } = string.Empty;

        public ConsolePromptTheme Theme { get; init; } = ConsolePromptTheme.Default;

        public ImmutableArray<string> BaseStatusBodyLines { get; init; } = [];

        public ImmutableArray<string> CommandPrefixes { get; init; } = ["/"];

        public ImmutableArray<ConsoleCommandSpec> CommandSpecs { get; init; } = [];

        public ImmutableDictionary<ConsoleSuggestionKind, ImmutableArray<ConsoleSuggestion>> StaticCandidates { get; init; } =
            ImmutableDictionary<ConsoleSuggestionKind, ImmutableArray<ConsoleSuggestion>>.Empty;

        public ImmutableDictionary<string, IConsoleParameterValueExplainer> ParameterExplainers { get; init; } =
            ImmutableDictionary<string, IConsoleParameterValueExplainer>.Empty.WithComparers(StringComparer.Ordinal);

        public IConsolePromptRuntimeResolver? RuntimeResolver { get; init; }

        public static ConsolePromptSpec CreatePlain(string? prompt = null) {
            return new ConsolePromptSpec {
                Purpose = ConsoleInputPurpose.Plain,
                Prompt = string.IsNullOrEmpty(prompt) ? "> " : prompt,
            };
        }

        public static ConsolePromptSpec CreateCommandLine(string? prompt = null) {
            return new ConsolePromptSpec {
                Purpose = ConsoleInputPurpose.CommandLine,
                Prompt = string.IsNullOrEmpty(prompt) ? "> " : prompt,
                BaseStatusBodyLines = [
                    "controls: Tab/Shift+Tab rotate suggestions",
                    "controls: Right accepts ghost completion",
                    "controls: Ctrl+Up/Ctrl+Down scroll status",
                ],
                StaticCandidates = ImmutableDictionary<ConsoleSuggestionKind, ImmutableArray<ConsoleSuggestion>>.Empty
                    .Add(ConsoleSuggestionKind.Boolean, ConsoleSuggestionCatalog.DefaultBooleanSuggestions),
            };
        }
    }

    internal sealed record ConsoleResolvedPrompt
    {
        public required ConsoleInputPurpose Purpose { get; init; }

        public required ServerContext? Server { get; init; }

        public required string Prompt { get; init; }

        public required string GhostText { get; init; }

        public required EmptySubmitBehavior EmptySubmitBehavior { get; init; }

        public required bool EnableCtrlEnterBypassGhostFallback { get; init; }

        public required string HelpText { get; init; }

        public required string InputSummary { get; init; }

        public required ConsolePromptTheme Theme { get; init; }

        public required ImmutableArray<string> StatusBodyLines { get; init; }

        public required ImmutableArray<string> CommandPrefixes { get; init; }

        public required ImmutableArray<ConsoleCommandSpec> CommandSpecs { get; init; }

        public required ImmutableDictionary<ConsoleSuggestionKind, ImmutableArray<ConsoleSuggestion>> Candidates { get; init; }

        public required ImmutableDictionary<string, IConsoleParameterValueExplainer> ParameterExplainers { get; init; }

        public ImmutableArray<ConsoleSuggestion> ResolveCandidates(ConsoleSuggestionKind target) {
            if (Candidates.TryGetValue(target, out ImmutableArray<ConsoleSuggestion> suggestions)) {
                return suggestions;
            }

            return ImmutableArray<ConsoleSuggestion>.Empty;
        }

        public IConsoleParameterValueExplainer? ResolveParameterExplainer(string semanticKey) {
            if (!string.IsNullOrWhiteSpace(semanticKey)
                && ParameterExplainers.TryGetValue(semanticKey, out IConsoleParameterValueExplainer? explainer)) {
                return explainer;
            }

            return null;
        }
    }
}
