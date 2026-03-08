namespace UnifierTSL.ConsoleClient.Shared.ConsolePrompting
{
    public sealed record ConsoleSuggestionPageState
    {
        public bool Enabled { get; init; }

        public int TotalCandidateCount { get; init; }

        public int WindowOffset { get; init; }

        public int PageSize { get; init; } = 80;

        public int PrefetchThreshold { get; init; } = 20;

        public int SelectedWindowIndex { get; init; }
    }
}
