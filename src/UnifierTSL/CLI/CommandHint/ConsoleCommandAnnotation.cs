namespace UnifierTSL.CLI.CommandHint
{
    public sealed class ConsoleCommandAnnotation
    {
        public string PrimaryName { get; init; } = string.Empty;

        public IReadOnlyList<string> Aliases { get; init; } = [];

        public string ParameterHint { get; init; } = string.Empty;

        public IReadOnlyList<ConsoleCommandPatternAnnotation> Patterns { get; init; } = [];
    }
}
