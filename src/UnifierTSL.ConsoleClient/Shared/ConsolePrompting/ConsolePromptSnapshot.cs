namespace UnifierTSL.ConsoleClient.Shared.ConsolePrompting
{
    public sealed record ConsolePromptSnapshot
    {
        public ConsoleInputPurpose Purpose { get; init; } = ConsoleInputPurpose.Plain;

        public string Prompt { get; init; } = "> ";

        public string[] CommandPrefixes { get; init; } = [];

        public string GhostText { get; init; } = string.Empty;

        public EmptySubmitBehavior EmptySubmitBehavior { get; init; } = EmptySubmitBehavior.KeepInput;

        public bool EnableCtrlEnterBypassGhostFallback { get; init; } = true;

        public string InputSummary { get; init; } = string.Empty;

        public string[] StatusBodyLines { get; init; } = [];

        public ConsolePromptTheme Theme { get; init; } = ConsolePromptTheme.Default;

        public ConsoleSuggestionEntry[] Candidates { get; init; } = [];

        public static ConsolePromptSnapshot CreatePlain(string? prompt = null) {
            return new ConsolePromptSnapshot {
                Purpose = ConsoleInputPurpose.Plain,
                Prompt = string.IsNullOrEmpty(prompt) ? "> " : prompt,
            };
        }

        public static ConsolePromptSnapshot CreateCommandLine(string? prompt = null) {
            return new ConsolePromptSnapshot {
                Purpose = ConsoleInputPurpose.CommandLine,
                Prompt = string.IsNullOrEmpty(prompt) ? "> " : prompt,
                CommandPrefixes = ["/"],
            };
        }
    }
}
