using System.Collections.Immutable;
using UnifierTSL.ConsoleClient.Shell;

namespace UnifierTSL.CLI.Sessions
{
    public enum ReadLineMaterializationScenario : byte
    {
        LocalInteractive,
        ProtocolInitial,
        ProtocolReactive,
        NonInteractiveFallback,
    }

    public readonly record struct ReadLineTargetKey
    {
        public string Value { get; }

        public ReadLineTargetKey(string value)
        {
            string normalized = string.IsNullOrWhiteSpace(value)
                ? ReadLineTargetKeys.Plain.Value
                : value.Trim().ToLowerInvariant();
            Value = normalized;
        }

        public override string ToString()
        {
            return Value;
        }
    }

    public static class ReadLineTargetKeys
    {
        public static readonly ReadLineTargetKey Plain = new("plain");
        public static readonly ReadLineTargetKey Command = new("command");
        public static readonly ReadLineTargetKey Player = new("player");
        public static readonly ReadLineTargetKey Server = new("server");
        public static readonly ReadLineTargetKey Item = new("item");
        public static readonly ReadLineTargetKey Boolean = new("boolean");
        public static readonly ReadLineTargetKey Enum = new("enum");

        public static ImmutableArray<ReadLineSuggestion> DefaultBooleanSuggestions { get; } = [
            new ReadLineSuggestion("true"),
            new ReadLineSuggestion("false"),
            new ReadLineSuggestion("on"),
            new ReadLineSuggestion("off"),
            new ReadLineSuggestion("1"),
            new ReadLineSuggestion("0"),
        ];
    }

    public readonly record struct ReadLineSuggestion(string Value, int Weight = 0);

    public sealed record ConsoleCommandHint
    {
        public string PrimaryName { get; init; } = string.Empty;

        public ImmutableArray<string> Aliases { get; init; } = [];

        public string HelpText { get; init; } = string.Empty;

        public string ParameterHint { get; init; } = string.Empty;

        public ImmutableArray<ReadLineTargetKey> ParameterTargets { get; init; } = [];

        public ImmutableArray<ConsoleCommandPatternHint> ParameterPatterns { get; init; } = [];
    }

    public sealed record ConsoleCommandPatternHint
    {
        public ImmutableArray<string> SubCommands { get; init; } = [];

        public ImmutableArray<ConsoleCommandParameterDescriptor> Parameters { get; init; } = [];
    }

    public sealed record ConsoleCommandParameterDescriptor
    {
        public string Name { get; init; } = string.Empty;

        public ReadLineTargetKey Target { get; init; } = ReadLineTargetKeys.Plain;

        public bool Optional { get; init; }

        public bool Variadic { get; init; }

        public ImmutableArray<string> EnumCandidates { get; init; } = [];
    }

    public readonly record struct ReadLineResolveContext(
        ReadLineReactiveState State,
        ReadLineMaterializationScenario Scenario);

    public sealed record ReadLineDynamicPatch
    {
        public ImmutableDictionary<ReadLineTargetKey, ImmutableArray<ReadLineSuggestion>> CandidateOverrides { get; init; } =
            ImmutableDictionary<ReadLineTargetKey, ImmutableArray<ReadLineSuggestion>>.Empty;

        public ImmutableArray<string> AdditionalStatusLines { get; init; } = [];

        public string? GhostTextOverride { get; init; }

        public string? HelpTextOverride { get; init; }

        public string? ParameterHintOverride { get; init; }
    }

    public sealed record ReadLineContextSpec
    {
        public ConsoleInputPurpose Purpose { get; init; } = ConsoleInputPurpose.Plain;

        public string Prompt { get; init; } = "> ";

        public string GhostText { get; init; } = string.Empty;

        public EmptySubmitBehavior EmptySubmitBehavior { get; init; } = EmptySubmitBehavior.KeepInput;

        public bool EnableCtrlEnterBypassGhostFallback { get; init; } = true;

        public string HelpText { get; init; } = string.Empty;

        public string ParameterHint { get; init; } = string.Empty;

        public int StatusPanelHeight { get; init; } = 4;

        public bool AllowAnsiStatusEscapes { get; init; }

        public bool UsePrecomputedCandidates { get; init; }

        public bool SkipDerivedStatus { get; init; }

        public ImmutableArray<string> BaseStatusLines { get; init; } = [];

        public ImmutableArray<string> CommandPrefixes { get; init; } = ["/"];

        public ImmutableArray<ConsoleCommandHint> CommandHints { get; init; } = [];

        public ImmutableDictionary<ReadLineTargetKey, ImmutableArray<ReadLineSuggestion>> StaticCandidates { get; init; } =
            ImmutableDictionary<ReadLineTargetKey, ImmutableArray<ReadLineSuggestion>>.Empty;

        public Func<ReadLineResolveContext, ReadLineDynamicPatch>? DynamicResolver { get; init; }

        public static ReadLineContextSpec CreatePlain(string? prompt = null)
        {
            return new ReadLineContextSpec {
                Purpose = ConsoleInputPurpose.Plain,
                Prompt = string.IsNullOrEmpty(prompt) ? "> " : prompt,
            };
        }

        public static ReadLineContextSpec CreateCommandLine(string? prompt = null)
        {
            return new ReadLineContextSpec {
                Purpose = ConsoleInputPurpose.CommandLine,
                Prompt = string.IsNullOrEmpty(prompt) ? "> " : prompt,
                BaseStatusLines = [
                    "controls: Tab/Shift+Tab rotate suggestions",
                    "controls: Right accepts ghost completion",
                    "controls: Ctrl+Up/Ctrl+Down scroll status",
                ],
                StaticCandidates = ImmutableDictionary<ReadLineTargetKey, ImmutableArray<ReadLineSuggestion>>.Empty
                    .Add(ReadLineTargetKeys.Boolean, ReadLineTargetKeys.DefaultBooleanSuggestions),
            };
        }
    }

    internal sealed record ReadLineResolvedContext
    {
        public required ConsoleInputPurpose Purpose { get; init; }

        public required string Prompt { get; init; }

        public required string GhostText { get; init; }

        public required EmptySubmitBehavior EmptySubmitBehavior { get; init; }

        public required bool EnableCtrlEnterBypassGhostFallback { get; init; }

        public required string HelpText { get; init; }

        public required string ParameterHint { get; init; }

        public required int StatusPanelHeight { get; init; }

        public required bool AllowAnsiStatusEscapes { get; init; }

        public required bool UsePrecomputedCandidates { get; init; }

        public required bool SkipDerivedStatus { get; init; }

        public required ImmutableArray<string> StatusLines { get; init; }

        public required ImmutableArray<string> CommandPrefixes { get; init; }

        public required ImmutableArray<ConsoleCommandHint> CommandHints { get; init; }

        public required ImmutableDictionary<ReadLineTargetKey, ImmutableArray<ReadLineSuggestion>> Candidates { get; init; }

        public ImmutableArray<ReadLineSuggestion> ResolveCandidates(ReadLineTargetKey target)
        {
            if (Candidates.TryGetValue(target, out ImmutableArray<ReadLineSuggestion> suggestions)) {
                return suggestions;
            }

            return ImmutableArray<ReadLineSuggestion>.Empty;
        }
    }
}
