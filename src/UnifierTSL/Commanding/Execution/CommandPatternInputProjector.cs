using System.Collections.Immutable;

namespace UnifierTSL.Commanding.Execution
{
    internal readonly record struct CommandPatternTokenProjection<TFlag>(
        ImmutableArray<CommandInputToken> CompletedPositionalTokens,
        ImmutableArray<CommandInputToken> CompletedFlagTokens,
        CommandInputToken? CurrentRawToken,
        bool CurrentTokenIsFlag,
        ImmutableArray<TFlag> RecognizedFlags,
        ImmutableArray<TFlag> AvailableFlags,
        int ActiveArgumentIndex);

    internal static class CommandPatternInputProjector
    {
        public static ImmutableArray<string> NormalizeLiterals(IEnumerable<string> values) {
            return [.. values
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value.Trim())];
        }

        public static int ResolveCompletedTokenCount(int tokenCount, bool endsWithSpace) {
            return endsWithSpace
                ? tokenCount
                : Math.Max(0, tokenCount - 1);
        }

        public static ImmutableArray<string> GetCompletedTokenValues(
            IReadOnlyList<CommandInputToken> tokens,
            int completedCount) {
            var boundedCount = Math.Clamp(completedCount, 0, tokens.Count);
            return [.. tokens.Take(boundedCount).Select(static token => token.Value)];
        }

        public static bool MatchPattern(
            IReadOnlyList<CommandInputToken> tokens,
            int completedCount,
            IReadOnlyList<string> literals) {
            if (literals.Count == 0) {
                return true;
            }

            var completed = GetCompletedTokenValues(tokens, completedCount);
            if (completed.Length < literals.Count) {
                return MatchLiteralPrefix(completed, literals);
            }

            for (var index = 0; index < literals.Count; index++) {
                if (!completed[index].Equals(literals[index], StringComparison.OrdinalIgnoreCase)) {
                    return false;
                }
            }

            return true;
        }

        public static bool MatchLiteralPrefix(
            IReadOnlyList<string> completed,
            IReadOnlyList<string> literals) {
            if (completed.Count > literals.Count) {
                return false;
            }

            for (var index = 0; index < completed.Count; index++) {
                if (!completed[index].Equals(literals[index], StringComparison.OrdinalIgnoreCase)) {
                    return false;
                }
            }

            return true;
        }

        public static CommandPatternTokenProjection<TFlag> Project<TFlag>(
            IReadOnlyList<CommandInputToken> rawTokens,
            int completedRawCount,
            IReadOnlyList<TFlag> flags,
            Func<TFlag, string> getCanonicalToken,
            Func<TFlag, IReadOnlyList<string>> getTokens) {

            var boundedCompletedCount = Math.Clamp(completedRawCount, 0, rawTokens.Count);
            List<CommandInputToken> completedPositionalTokens = [];
            List<CommandInputToken> completedFlagTokens = [];
            List<TFlag> recognizedFlags = [];
            HashSet<string> recognizedCanonicalTokens = new(StringComparer.OrdinalIgnoreCase);

            foreach (var token in rawTokens.Take(boundedCompletedCount)) {
                if (TryResolveExactFlag(flags, token, getTokens, out var flag)) {
                    completedFlagTokens.Add(token);

                    var canonicalToken = getCanonicalToken(flag!);
                    if (recognizedCanonicalTokens.Add(canonicalToken)) {
                        recognizedFlags.Add(flag!);
                    }

                    continue;
                }

                completedPositionalTokens.Add(token);
            }

            CommandInputToken? currentRawToken = boundedCompletedCount < rawTokens.Count
                ? rawTokens[boundedCompletedCount]
                : null;
            var currentTokenIsFlag = currentRawToken is CommandInputToken current
                && IsFlagInputToken(flags, current, getTokens);
            ImmutableArray<TFlag> availableFlags = [.. flags.Where(flag =>
                !recognizedCanonicalTokens.Contains(getCanonicalToken(flag)))];

            return new CommandPatternTokenProjection<TFlag>(
                CompletedPositionalTokens: [.. completedPositionalTokens],
                CompletedFlagTokens: [.. completedFlagTokens],
                CurrentRawToken: currentRawToken,
                CurrentTokenIsFlag: currentTokenIsFlag,
                RecognizedFlags: [.. recognizedFlags],
                AvailableFlags: availableFlags,
                ActiveArgumentIndex: completedPositionalTokens.Count);
        }

        private static bool TryResolveExactFlag<TFlag>(
            IReadOnlyList<TFlag> flags,
            CommandInputToken token,
            Func<TFlag, IReadOnlyList<string>> getTokens,
            out TFlag? flag) {
            flag = default;
            if (token.Quoted
                || token.LeadingCharacterEscaped
                || !token.Value.StartsWith("-", StringComparison.Ordinal)) {
                return false;
            }

            foreach (var candidate in flags) {
                if (getTokens(candidate).Any(flagToken => flagToken.Equals(token.Value, StringComparison.OrdinalIgnoreCase))) {
                    flag = candidate;
                    return true;
                }
            }

            return false;
        }

        private static bool IsFlagInputToken<TFlag>(
            IReadOnlyList<TFlag> flags,
            CommandInputToken token,
            Func<TFlag, IReadOnlyList<string>> getTokens) {
            if (token.Quoted
                || token.LeadingCharacterEscaped
                || !token.Value.StartsWith("-", StringComparison.Ordinal)) {
                return false;
            }

            return flags.Any(flag => getTokens(flag).Any(flagToken =>
                flagToken.Equals(token.Value, StringComparison.OrdinalIgnoreCase)
                || flagToken.StartsWith(token.Value, StringComparison.OrdinalIgnoreCase)));
        }
    }
}
