namespace UnifierTSL.ConsoleClient.Shared.ConsolePrompting
{
    public enum ConsoleInputPurpose : byte
    {
        Plain,
        StartupPort,
        StartupPassword,
        CommandLine,
    }

    public enum EmptySubmitBehavior : byte
    {
        KeepInput,
        AcceptGhostIfAvailable,
    }

    public sealed record ConsoleSuggestionEntry
    {
        public string Value { get; init; } = string.Empty;
    }

    public sealed record ConsoleInputState
    {
        public ConsoleInputPurpose Purpose { get; init; } = ConsoleInputPurpose.Plain;

        public string InputText { get; init; } = string.Empty;

        public int CursorIndex { get; init; }

        public int CompletionIndex { get; init; }

        public int CompletionCount { get; init; }

        public int CandidateWindowOffset { get; init; }
    }
}
