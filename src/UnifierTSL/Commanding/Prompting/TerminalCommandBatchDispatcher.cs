using System.IO;

namespace UnifierTSL.Commanding.Prompting
{
    public static class TerminalCommandBatchDispatcher
    {
        public static IReadOnlyList<string> SplitNonEmptyLines(string? bufferText) {
            List<string> lines = [];
            using StringReader reader = new(bufferText ?? string.Empty);
            while (reader.ReadLine() is { } line) {
                if (!string.IsNullOrWhiteSpace(line)) {
                    lines.Add(line);
                }
            }

            return lines;
        }

        public static async Task<CommandDispatchResult> DispatchSequentialAsync(
            IReadOnlyList<string> lines,
            Func<string, CancellationToken, Task<CommandDispatchResult>> dispatchLineAsync,
            Action onBatchAborted,
            Func<CommandDispatchResult, bool>? isFailure = null,
            CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(lines);
            ArgumentNullException.ThrowIfNull(dispatchLineAsync);
            ArgumentNullException.ThrowIfNull(onBatchAborted);

            if (lines.Count == 0) {
                return new CommandDispatchResult {
                    Handled = false,
                    Matched = false,
                };
            }

            CommandDispatchResult result = new() {
                Handled = false,
                Matched = false,
            };
            var isFailureEvaluator = isFailure ?? IsFailure;

            for (var i = 0; i < lines.Count; i++) {
                cancellationToken.ThrowIfCancellationRequested();
                try {
                    result = await dispatchLineAsync(lines[i], cancellationToken).ConfigureAwait(false);
                }
                catch {
                    onBatchAborted();
                    throw;
                }

                if (!isFailureEvaluator(result)) {
                    continue;
                }

                onBatchAborted();
                return result;
            }

            return result;
        }

        private static bool IsFailure(CommandDispatchResult result) {
            return !result.Handled
                || !result.Matched
                || !(result.Outcome?.Succeeded ?? true);
        }
    }
}
