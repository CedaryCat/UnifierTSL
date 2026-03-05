namespace UnifierTSL.CLI.CommandHint
{
    public sealed class ConsoleCommandPatternAnnotation
    {
        public IReadOnlyList<string> SubCommands { get; init; } = [];

        public IReadOnlyList<ConsoleCommandParameterAnnotation> Parameters { get; init; } = [];
    }
}
