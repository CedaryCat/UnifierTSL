namespace UnifierTSL.ConsoleClient.Shell
{
    public sealed class ReadLineCandidateView
    {
        public string Value { get; set; } = string.Empty;

        public int Weight { get; set; }

        public bool IsSelected { get; set; }
    }

    public sealed class ReadLineStatusView
    {
        public string Text { get; set; } = string.Empty;

        public bool IsHeader { get; set; }
    }

    public sealed class ReadLineViewModel
    {
        public ConsoleInputPurpose Purpose { get; set; } = ConsoleInputPurpose.Plain;

        public string Prompt { get; set; } = "> ";

        public string InputText { get; set; } = string.Empty;

        public string GhostText { get; set; } = string.Empty;

        public int CursorIndex { get; set; }

        public int CompletionIndex { get; set; }

        public int CompletionCount { get; set; }

        public List<ReadLineStatusView> StatusLines { get; set; } = [];

        public List<ReadLineCandidateView> Candidates { get; set; } = [];
    }
}
