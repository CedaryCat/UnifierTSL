namespace UnifierTSL.Contracts.Display {
    public sealed class EditorMaterialState {
        public InlineSegments Content { get; init; } = new();
        public InlineSegments Prompt { get; init; } = new() {
            Text = "> ",
            Highlights = [
                new HighlightSpan {
                    StartIndex = 0,
                    Length = 2,
                    StyleId = SurfaceStyleCatalog.PromptLabel,
                },
            ],
        };
        public GhostInlineHint GhostHint { get; init; } = new();
        public EditorSubmitBehavior SubmitBehavior { get; init; } = new();
    }
}
