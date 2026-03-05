namespace UnifierTSL.ConsoleClient.Shell
{
    public sealed class ReadLineSnapshotPayload
    {
        public ConsoleInputPurpose Purpose { get; set; } = ConsoleInputPurpose.Plain;

        public string Prompt { get; set; } = "> ";

        public string GhostText { get; set; } = string.Empty;

        public EmptySubmitBehavior EmptySubmitBehavior { get; set; } = EmptySubmitBehavior.KeepInput;

        public bool EnableCtrlEnterBypassGhostFallback { get; set; } = true;

        public string HelpText { get; set; } = string.Empty;

        public string ParameterHint { get; set; } = string.Empty;

        public int StatusPanelHeight { get; set; } = 4;

        public List<string> StatusLines { get; set; } = [];

        public bool AllowAnsiStatusEscapes { get; set; }

        public List<ConsoleSuggestionItem> Candidates { get; set; } = [];

        public static ReadLineSnapshotPayload CreatePlain(string? prompt = null)
        {
            return new ReadLineSnapshotPayload {
                Purpose = ConsoleInputPurpose.Plain,
                Prompt = string.IsNullOrEmpty(prompt) ? "> " : prompt,
            };
        }

        public static ReadLineSnapshotPayload CreateCommandLine(string? prompt = null)
        {
            return new ReadLineSnapshotPayload {
                Purpose = ConsoleInputPurpose.CommandLine,
                Prompt = string.IsNullOrEmpty(prompt) ? "> " : prompt,
            };
        }
    }
}
