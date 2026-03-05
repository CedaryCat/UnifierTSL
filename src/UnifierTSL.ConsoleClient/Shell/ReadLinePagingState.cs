namespace UnifierTSL.ConsoleClient.Shell
{
    public sealed class ReadLinePagingState
    {
        public bool Enabled { get; set; }

        public int TotalCandidateCount { get; set; }

        public int WindowOffset { get; set; }

        public int PageSize { get; set; } = 30;

        public int PrefetchThreshold { get; set; } = 5;

        public int SelectedWindowIndex { get; set; }
    }
}
