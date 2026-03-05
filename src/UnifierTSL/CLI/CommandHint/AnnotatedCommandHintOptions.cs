namespace UnifierTSL.CLI.CommandHint
{
    public sealed class AnnotatedCommandHintOptions
    {
        public Func<IReadOnlyList<string>> CommandPrefixResolver { get; init; } = static () => ["/"];

        public Func<IReadOnlyList<ConsoleCommandRuntimeDescriptor>> CommandResolver { get; init; } = static () => [];

        public Func<IReadOnlyList<string>> PlayerCandidateResolver { get; init; } = static () => [];

        public Func<IReadOnlyList<string>> ServerCandidateResolver { get; init; } = static () => [];

        public Func<IReadOnlyList<string>> ItemCandidateResolver { get; init; } = static () => [];

        public Func<IReadOnlyList<ConsoleCommandAnnotation>> AnnotationResolver { get; init; } = static () => [];

        public bool AllowAnsiStatusEscapes { get; init; }
    }
}
