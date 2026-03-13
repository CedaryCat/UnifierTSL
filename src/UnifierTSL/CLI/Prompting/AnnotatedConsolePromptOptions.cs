using System.Collections.Immutable;

namespace UnifierTSL.CLI.Prompting
{
    public sealed class AnnotatedConsolePromptOptions
    {
        public Func<IReadOnlyList<string>> CommandPrefixResolver { get; init; } = static () => ["/"];

        public Func<IReadOnlyList<ConsoleCommandSpec>> CommandSpecResolver { get; init; } = static () => [];

        public Func<IReadOnlyList<string>> PlayerCandidateResolver { get; init; } = static () => ConsolePromptCommonObjects.GetPlayerCandidates();

        public Func<IReadOnlyList<string>> ServerCandidateResolver { get; init; } = ConsolePromptCommonObjects.GetServerCandidates;

        public Func<IReadOnlyList<string>> ItemCandidateResolver { get; init; } = ConsolePromptCommonObjects.GetItemCandidates;

        public Func<IReadOnlyDictionary<string, IConsoleParameterValueExplainer>> ParameterExplainerResolver { get; init; } =
            static () => ImmutableDictionary<string, IConsoleParameterValueExplainer>.Empty;
    }
}
