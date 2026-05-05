using UnifierTSL.Commanding;
using UnifierTSL.Commanding.Composition;
using UnifierTSL.Commanding.Endpoints;
using UnifierTSL.Commanding.Prompting;
using UnifierTSL.Events.Handlers;

namespace TShockAPI.Commanding
{
    internal sealed class TShockTerminalCommandDispatchAdapter : ITerminalCommandDispatchAdapter
    {
        public int Priority => -100;

        public bool CanHandle(MessageSender sender) {
            return sender.IsServer;
        }

        public Task<CommandDispatchResult> DispatchAsync(
            MessageSender sender,
            string rawInput,
            IReadOnlyList<string> _,
            CancellationToken cancellationToken = default) {
            if (!CommandPromptSettings.EnableMultiLineCommandInput || !ContainsLineBreak(rawInput)) {
                return DispatchSingleAsync(sender, rawInput, cancellationToken);
            }

            var lines = TerminalCommandBatchDispatcher.SplitNonEmptyLines(rawInput);
            return lines.Count switch {
                0 => Task.FromResult(new CommandDispatchResult {
                    Handled = false,
                    Matched = false,
                }),
                1 => DispatchBatchLineAsync(sender, lines[0], cancellationToken),
                _ => TerminalCommandBatchDispatcher.DispatchSequentialAsync(
                    lines,
                    (line, token) => DispatchBatchLineAsync(sender, line, token),
                    () => WriteBatchAborted(sender),
                    result => IsBatchFailure(sender, result),
                    cancellationToken),
            };
        }

        private static async Task<CommandDispatchResult> DispatchSingleAsync(
            MessageSender sender,
            string rawInput,
            CancellationToken cancellationToken) {
            var executor = new CommandExecutor(sender.SourceServer, byte.MaxValue);
            var silent = TSCommandBridge.IsSilentInvocation(rawInput);
            var request = TSCommandBridge.CreateDispatchRequest(
                executor,
                TerminalCommandEndpoint.EndpointId,
                rawInput,
                silent);
            using var activityScope = TSCommandBridge.BeginTerminalDispatchActivityScope(request, cancellationToken);
            var dispatchCancellationToken = activityScope.Activity is null
                ? cancellationToken
                : activityScope.CancellationToken;
            var result = await CommandDispatchCoordinator.DispatchAsync(request, dispatchCancellationToken).ConfigureAwait(false);
            return TSCommandBridge.CompleteTerminalDispatch(executor, request, result);
        }

        private static async Task<CommandDispatchResult> DispatchBatchLineAsync(
            MessageSender sender,
            string rawInput,
            CancellationToken cancellationToken) {
            var result = await DispatchSingleAsync(sender, rawInput, cancellationToken).ConfigureAwait(false);
            if (result.Handled) {
                return result;
            }

            var executor = new CommandExecutor(sender.SourceServer, byte.MaxValue);
            var outcome = CommandOutcome.Error(GetString("Invalid command entered. Type {0}help for a list of valid commands.", Commands.Specifier));
            CommandSystem.GetOutcomeWriter<CommandExecutor>().Write(executor, outcome);
            return new CommandDispatchResult {
                Handled = true,
                Matched = false,
                Outcome = outcome,
            };
        }

        private static bool IsBatchFailure(MessageSender sender, CommandDispatchResult result) {
            if (!result.Handled || !(result.Outcome?.Succeeded ?? true)) {
                return true;
            }

            return !result.Matched && !UsesSuccessfulLegacyFallback(sender, result);
        }

        private static bool UsesSuccessfulLegacyFallback(MessageSender sender, CommandDispatchResult result) {
            // Terminal dispatch preserves Matched=false after the legacy TShock fallback executes.
            // Batch abort needs to treat that legacy path as success without changing global result semantics.
            _ = sender;
            return result.Handled
                && !result.Matched
                && Commands.IsLegacyCommandRegistered(result.ExecutionRequest?.InvokedRoot ?? string.Empty);
        }

        private static void WriteBatchAborted(MessageSender sender) {
            var executor = new CommandExecutor(sender.SourceServer, byte.MaxValue);
            CommandSystem.GetOutcomeWriter<CommandExecutor>().Write(
                executor,
                CommandOutcome.Warning(GetString("batch aborted")));
        }

        private static bool ContainsLineBreak(string? rawInput) {
            return !string.IsNullOrEmpty(rawInput)
                && (rawInput.Contains('\n') || rawInput.Contains('\r'));
        }
    }
}
