namespace UnifierTSL.ConsoleClient.Shell
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

    public sealed class ConsoleSuggestionItem
    {
        public string Value { get; set; } = string.Empty;

        public int Weight { get; set; }
    }

    public sealed class ReadLineReactiveState
    {
        public ConsoleInputPurpose Purpose { get; set; } = ConsoleInputPurpose.Plain;

        public string InputText { get; set; } = string.Empty;

        public int CursorIndex { get; set; }

        public int CompletionIndex { get; set; }

        public int CompletionCount { get; set; }

        public int CandidateWindowOffset { get; set; }
    }
}
