namespace UnifierTSL.ConsoleClient.Shared.ConsolePrompting
{
    public sealed record ConsoleRenderSnapshot
    {
        public ConsolePromptSnapshot Payload { get; init; } = ConsolePromptSnapshot.CreatePlain();

        public ConsoleSuggestionPageState Paging { get; init; } = new();

        public static ConsoleRenderSnapshot CreatePlain(string? prompt = null) {
            return new ConsoleRenderSnapshot {
                Payload = ConsolePromptSnapshot.CreatePlain(prompt),
                Paging = new ConsoleSuggestionPageState(),
            };
        }

        public static ConsoleRenderSnapshot CreateCommandLine(string? prompt = null) {
            return new ConsoleRenderSnapshot {
                Payload = ConsolePromptSnapshot.CreateCommandLine(prompt),
                Paging = new ConsoleSuggestionPageState(),
            };
        }
    }
}
