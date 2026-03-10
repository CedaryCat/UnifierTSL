namespace UnifierTSL.ConsoleClient.Shared.ConsolePrompting
{
    public sealed record ConsolePromptTheme
    {
        public ConsoleColor PromptForeground { get; init; } = ConsoleColor.Green;

        public ConsoleColor InputForeground { get; init; } = ConsoleColor.White;

        public ConsoleColor CommandForeground { get; init; } = ConsoleColor.Yellow;

        public ConsoleColor GhostForeground { get; init; } = ConsoleColor.DarkGray;

        public ConsoleColor SuggestionBadgeForeground { get; init; } = ConsoleColor.DarkGray;

        public bool UseVividStatusBar { get; init; } = true;

        public ConsoleColor StatusBarForeground { get; init; } = ConsoleColor.Black;

        public ConsoleColor StatusBarBackground { get; init; } = ConsoleColor.Cyan;

        public ConsoleColor VividStatusBarForeground { get; init; } = ConsoleColor.White;

        public ConsoleColor VividStatusBarBackground { get; init; } = ConsoleColor.DarkCyan;

        public ConsoleColor StatusDetailForeground { get; init; } = ConsoleColor.DarkGray;

        public static ConsolePromptTheme Default { get; } = new();
    }
}
