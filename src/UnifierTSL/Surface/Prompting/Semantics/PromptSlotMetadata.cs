using System.Collections.Immutable;

namespace UnifierTSL.Surface.Prompting.Semantics;

/*
    Prompt slot metadata is intentionally opaque to core prompt infrastructure.

    The core only carries these key/value entries from command-binding projection into the active
    slot context so downstream providers can mirror provider-specific runtime quirks without
    minting semantic-key dialects. Keep the transport generic: the core must not interpret or
    branch on provider-owned keys.
*/
public readonly record struct PromptSlotMetadataEntry(string Key, string Value);

public static class PromptSlotMetadata
{
    public static ImmutableArray<PromptSlotMetadataEntry> Normalize(IEnumerable<PromptSlotMetadataEntry>? entries) {
        if (entries is null) {
            return [];
        }

        Dictionary<string, PromptSlotMetadataEntry> normalized =
            new(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries) {
            var key = entry.Key?.Trim() ?? string.Empty;
            if (key.Length == 0) {
                continue;
            }

            normalized[key] = new PromptSlotMetadataEntry(key, entry.Value ?? string.Empty);
        }

        return [.. normalized.Values.OrderBy(static entry => entry.Key, StringComparer.OrdinalIgnoreCase)];
    }

    public static bool TryGetValue(ImmutableArray<PromptSlotMetadataEntry> entries, string key, out string value) {

        if (!string.IsNullOrWhiteSpace(key)) {
            foreach (var entry in entries) {
                if (entry.Key.Equals(key, StringComparison.OrdinalIgnoreCase)) {
                    value = entry.Value;
                    return true;
                }
            }
        }

        value = string.Empty;
        return false;
    }

    public static bool ContentEquals(ImmutableArray<PromptSlotMetadataEntry> left, ImmutableArray<PromptSlotMetadataEntry> right) {
        if (left.Length != right.Length) {
            return false;
        }

        for (var index = 0; index < left.Length; index++) {
            if (!left[index].Key.Equals(right[index].Key, StringComparison.OrdinalIgnoreCase)
                || !left[index].Value.Equals(right[index].Value, StringComparison.Ordinal)) {
                return false;
            }
        }

        return true;
    }
}
