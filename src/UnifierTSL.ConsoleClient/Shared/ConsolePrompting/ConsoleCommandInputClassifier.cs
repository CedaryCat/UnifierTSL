namespace UnifierTSL.ConsoleClient.Shared.ConsolePrompting
{
    public readonly record struct ConsoleCommandTokenSpan(int StartIndex, int Length)
    {
        public int EndIndex => StartIndex + Length;
    }

    public static class ConsoleCommandInputClassifier
    {
        public static bool TryFindCommandTokenSpan(string? text, IReadOnlyList<string>? commandPrefixes, out ConsoleCommandTokenSpan span) {
            span = default;

            string value = text ?? string.Empty;
            if (value.Length == 0) {
                return false;
            }

            int leadingWhitespaceLength = 0;
            while (leadingWhitespaceLength < value.Length && char.IsWhiteSpace(value[leadingWhitespaceLength])) {
                leadingWhitespaceLength += 1;
            }

            if (leadingWhitespaceLength >= value.Length) {
                return false;
            }

            int prefixLength = ResolvePrefixLength(value.AsSpan(leadingWhitespaceLength), commandPrefixes);
            int commandStart = leadingWhitespaceLength + prefixLength;
            if (commandStart >= value.Length || char.IsWhiteSpace(value[commandStart])) {
                return false;
            }

            int commandLength = 0;
            while ((commandStart + commandLength) < value.Length && !char.IsWhiteSpace(value[commandStart + commandLength])) {
                commandLength += 1;
            }

            if (commandLength <= 0) {
                return false;
            }

            span = new ConsoleCommandTokenSpan(leadingWhitespaceLength, prefixLength + commandLength);
            return true;
        }

        private static int ResolvePrefixLength(ReadOnlySpan<char> remaining, IReadOnlyList<string>? commandPrefixes) {
            if (remaining.Length == 0 || commandPrefixes is null || commandPrefixes.Count == 0) {
                return 0;
            }

            int bestLength = 0;
            foreach (string prefix in commandPrefixes) {
                if (string.IsNullOrWhiteSpace(prefix)) {
                    continue;
                }

                if (prefix.Length <= bestLength) {
                    continue;
                }

                if (remaining.StartsWith(prefix.AsSpan(), StringComparison.OrdinalIgnoreCase)) {
                    bestLength = prefix.Length;
                }
            }

            return bestLength;
        }
    }
}
