using UnifierTSL.Surface.Prompting;
using UnifierTSL.Surface.Prompting.Model;
using UnifierTSL.Events.Handlers;
using UnifierTSL.Servers;

namespace UnifierTSL.Surface.Status
{
    internal static class PromptSummaryComposer
    {
        public static string Compose(ServerContext? server, PromptResolveContext resolveContext) {
            var purpose = resolveContext.Purpose == PromptInputPurpose.Plain
                ? PromptInputPurpose.CommandLine
                : resolveContext.Purpose;
            var summary = purpose.ToString();

            BuildSurfacePromptSummaryEvent args = new(
                server: server,
                purpose: purpose,
                state: resolveContext.State,
                scenario: resolveContext.Scenario,
                inputSummary: summary);
            try {
                UnifierApi.EventHub.Launcher.BuildSurfacePromptSummary.Invoke(ref args);
            }
            catch {
            }

            return args.InputSummary ?? string.Empty;
        }
    }
}
