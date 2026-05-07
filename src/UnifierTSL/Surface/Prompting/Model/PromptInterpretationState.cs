namespace UnifierTSL.Surface.Prompting.Model {
    public sealed class PromptInterpretationState {
        public static PromptInterpretationState Empty { get; } = new();

        public PromptInterpretationPresentation Presentation { get; init; } = new();

        public string ActiveInterpretationId { get; init; } = string.Empty;

        public int ActiveInterpretationIndex { get; init; } = -1;

        public PromptInterpretation[] Interpretations { get; init; } = [];
    }

    public sealed class PromptInterpretation {
        public string Id { get; init; } = string.Empty;

        public string Label { get; init; } = string.Empty;

        public PromptStyledText Summary { get; init; } = new();

        public PromptInterpretationSection[] Sections { get; init; } = [];
    }

    public sealed class PromptInterpretationSection {
        public string Label { get; init; } = string.Empty;

        public PromptStyledText[] Lines { get; init; } = [];
    }
}
