namespace UnifierTSL.Surface.Prompting.Model {
    public sealed record PromptInterpretationPresentation {
        public bool SuppressesCompletionPreview { get; init; }

        public bool PrefersExpandedDetail { get; init; }
    }
}
