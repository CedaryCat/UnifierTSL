using UnifierTSL.Commanding;

namespace ExamplePlugin
{
    [CommandController("exampletask", Summary = nameof(ControllerSummary))]
    [Aliases("simtask")]
    internal static class ExampleSimulatedTaskCommand
    {
        private static string ControllerSummary => "Runs a cancellable simulated terminal task.";
        private static string ExecuteSummary => "Runs a long simulated task with progress updates.";
        private static string StepsOutOfRangeMessage => "Steps must be between 1 and 240.";
        private static string DelayMsOutOfRangeMessage => "Delay must be between 50 and 10000 milliseconds.";

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [TerminalCommand]
        public static async Task<CommandOutcome> Execute(
            [Int32Value(Minimum = 1, Maximum = 240, OutOfRangeMessage = nameof(StepsOutOfRangeMessage))] int steps = 20,
            [Int32Value(Minimum = 50, Maximum = 10000, OutOfRangeMessage = nameof(DelayMsOutOfRangeMessage))] int delayMs = 500,
            [FromAmbientContext] ICommandExecutionFeedback? feedback = null,
            CancellationToken cancellationToken = default) {
            var completed = 0;
            var effectiveCancellationToken = cancellationToken.CanBeCanceled || feedback is null
                ? cancellationToken
                : feedback.CancellationToken;

            try {
                feedback?.SetStatus($"Starting simulated task with {steps} step(s).");
                feedback?.SetProgress(0, steps);
                for (var step = 1; step <= steps; step++) {
                    feedback?.SetStatus($"Running simulated step {step}/{steps}...");
                    await Task.Delay(delayMs, effectiveCancellationToken);
                    completed = step;
                    feedback?.SetProgress(completed, steps);
                }

                feedback?.SetStatus($"Completed simulated task with {steps} step(s).");
                return CommandOutcome.Success($"Simulated terminal task completed {steps} step(s) with {delayMs}ms delay.");
            }
            catch (OperationCanceledException) when (effectiveCancellationToken.IsCancellationRequested) {
                feedback?.SetStatus($"Canceled after {completed}/{steps} step(s).");
                return CommandOutcome.Warning($"Simulated terminal task canceled after {completed}/{steps} step(s).");
            }
            finally {
                feedback?.ClearProgress();
            }
        }
    }
}
