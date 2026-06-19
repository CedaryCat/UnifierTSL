using UnifierTSL.Commanding;

namespace ExamplePlugin.Features
{
    [CommandController("exampletask")]
    public static class ExampleFeatureTaskCommand
    {
        private static string EchoSummary => "Echoes text from a satellite plugin action.";

        [CommandAction("feature", Summary = nameof(EchoSummary))]
        [TerminalCommand]
        public static CommandOutcome Echo([RemainingText] string text = "") {
            return string.IsNullOrWhiteSpace(text)
                ? CommandOutcome.Info("ExamplePlugin.Features added this action to ExamplePlugin's command root.")
                : CommandOutcome.Info($"ExamplePlugin.Features received: {text.Trim()}");
        }
    }
}
