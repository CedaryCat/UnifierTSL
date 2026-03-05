namespace UnifierTSL.CLI.CommandHint
{
    public sealed class ConsoleCommandRuntimeDescriptor
    {
        public string PrimaryName { get; init; } = string.Empty;

        public IReadOnlyList<string> Aliases { get; init; } = [];

        public string HelpText { get; init; } = string.Empty;
    }
}
