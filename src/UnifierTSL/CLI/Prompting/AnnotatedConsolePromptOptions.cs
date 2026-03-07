namespace UnifierTSL.CLI.Prompting
{
    public sealed class AnnotatedConsolePromptOptions
    {
        public Func<IReadOnlyList<string>> CommandPrefixResolver { get; init; } = static () => ["/"];

        public Func<IReadOnlyList<ConsoleCommandSpec>> CommandSpecResolver { get; init; } = static () => [];

        public Func<IReadOnlyList<string>> PlayerCandidateResolver { get; init; } = static () => [];

        public Func<IReadOnlyList<string>> ServerCandidateResolver { get; init; } = static () => [];

        public Func<IReadOnlyList<string>> ItemCandidateResolver { get; init; } = static () => [];
    }
}
