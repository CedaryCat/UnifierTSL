namespace UnifierTSL.CLI;

/// <summary>
/// Identifies a compatibility override call site using the form "Caller#OccurrenceIndex@Detail".
/// </summary>
public readonly record struct ConsolePromptOverrideKey(string Caller, int OccurrenceIndex, string? Detail = null)
{
    public static ConsolePromptOverrideKey Parse(string scope) {
        if (!TryParse(scope, out ConsolePromptOverrideKey key)) {
            throw new FormatException(
                $"Invalid console prompt override scope '{scope}'. Expected format 'Caller#OccurrenceIndex' or 'Caller#OccurrenceIndex@Detail'.");
        }

        return key;
    }

    public static bool TryParse(string? scope, out ConsolePromptOverrideKey key) {
        key = default;
        if (string.IsNullOrWhiteSpace(scope)) {
            return false;
        }

        string normalized = scope.Trim();
        int detailSeparatorIndex = normalized.IndexOf('@', StringComparison.Ordinal);
        string scopeWithoutDetail = normalized;
        string? detail = null;
        if (detailSeparatorIndex >= 0) {
            scopeWithoutDetail = normalized[..detailSeparatorIndex];
            detail = normalized[(detailSeparatorIndex + 1)..].Trim();
            if (detail.Length == 0) {
                return false;
            }
        }

        int occurrenceSeparatorIndex = scopeWithoutDetail.LastIndexOf('#');
        if (occurrenceSeparatorIndex <= 0 || occurrenceSeparatorIndex >= scopeWithoutDetail.Length - 1) {
            return false;
        }

        string caller = scopeWithoutDetail[..occurrenceSeparatorIndex].Trim();
        if (caller.Length == 0) {
            return false;
        }

        string occurrenceText = scopeWithoutDetail[(occurrenceSeparatorIndex + 1)..].Trim();
        if (!int.TryParse(occurrenceText, out int occurrenceIndex) || occurrenceIndex < 0) {
            return false;
        }

        key = new ConsolePromptOverrideKey(caller, occurrenceIndex, detail);
        return true;
    }

    public override string ToString() {
        if (string.IsNullOrWhiteSpace(Detail)) {
            return $"{Caller}#{OccurrenceIndex}";
        }

        return $"{Caller}#{OccurrenceIndex}@{Detail}";
    }
}
