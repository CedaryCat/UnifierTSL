namespace UnifierTSL.ConsoleClient.Shared.ConsolePrompting
{
    public sealed record ConsoleSuggestionPageState
    {
        public bool Enabled { get; init; }

        public int TotalCandidateCount { get; init; }

        public int WindowOffset { get; init; }

        public int PageSize { get; init; } = 30;

        public int PrefetchThreshold { get; init; } = 5;

        public int SelectedWindowIndex { get; init; }
    }
}
