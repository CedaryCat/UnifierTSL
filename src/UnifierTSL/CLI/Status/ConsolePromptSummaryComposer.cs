using UnifierTSL.CLI.Prompting;
using UnifierTSL.ConsoleClient.Shared.ConsolePrompting;
using UnifierTSL.Events.Handlers;
using UnifierTSL.Servers;

namespace UnifierTSL.CLI.Status
{
    internal static class ConsolePromptSummaryComposer
    {
        public static string Compose(ServerContext? server, ConsolePromptResolveContext resolveContext) {
            ConsoleInputPurpose purpose = resolveContext.State.Purpose == ConsoleInputPurpose.Plain
                ? ConsoleInputPurpose.CommandLine
                : resolveContext.State.Purpose;
            string summary = purpose.ToString();

            BuildConsolePromptSummaryEvent args = new(
                server: server,
                purpose: purpose,
                state: resolveContext.State,
                scenario: resolveContext.Scenario,
                inputSummary: summary);
            try {
                UnifierApi.EventHub.Launcher.BuildConsolePromptSummary.Invoke(ref args);
            }
            catch {
            }

            return args.InputSummary ?? string.Empty;
        }
    }
}
