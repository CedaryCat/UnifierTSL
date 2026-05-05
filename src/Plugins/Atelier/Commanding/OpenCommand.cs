using Atelier.Session.Context;
using UnifierTSL.Commanding;

namespace Atelier.Commanding
{
    [CommandController("repl", Summary = nameof(ControllerSummary))]
    internal static class OpenCommand
    {
        private static string ControllerSummary => GetString("Opens the Atelier REPL window.");
        private static string ExecuteSummary => GetString("Opens the Atelier REPL window.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [TerminalCommand]
        public static CommandOutcome Execute(
            [RemainingArgs] string[] args,
            CommandInvocationContext context,
            CancellationToken cancellationToken = default) {
            if (AtelierPlugin.WindowService is not { } windowService) {
                return CommandOutcome.Error(GetString("Atelier plugin is not initialized."));
            }

            if (!TargetResolver.TryResolve(context, args, out var options, out var failure)) {
                return failure ?? CommandOutcome.Error(GetString("Unable to resolve Atelier target."));
            }

            return windowService.Open(options!, cancellationToken);
        }
    }
}
