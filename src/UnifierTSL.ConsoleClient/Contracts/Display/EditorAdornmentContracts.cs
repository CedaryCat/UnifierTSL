namespace UnifierTSL.Contracts.Display {
    public sealed class InlineTextInsertion {
        public int SourceIndex { get; init; }
        public InlineSegments Content { get; init; } = new();
    }

    public sealed class GhostInlineHint {
        public string SourceCompletionId { get; init; } = string.Empty;
        public InlineTextInsertion[] Insertions { get; init; } = [];
    }

    public static class GhostInlineHintOps {
        public static bool TryApply(string? sourceText, GhostInlineHint? hint, out string result) {
            return TryApply(sourceText, hint, out result, out _);
        }

        public static bool TryApply(string? sourceText, GhostInlineHint? hint, out string result, out int caretIndex) {
            var source = sourceText ?? string.Empty;
            result = source;
            caretIndex = source.Length;
            if (hint is null || hint.Insertions is not { Length: > 0 } insertions) {
                return false;
            }

            System.Text.StringBuilder builder = new(source.Length);
            bool insertedAny = false;
            for (int sourceIndex = 0; sourceIndex <= source.Length; sourceIndex++) {
                bool appendedAtBoundary = false;
                foreach (var insertion in insertions) {
                    if (insertion is null
                        || Math.Clamp(insertion.SourceIndex, 0, source.Length) != sourceIndex
                        || string.IsNullOrEmpty(insertion.Content.Text)) {
                        continue;
                    }

                    builder.Append(insertion.Content.Text);
                    insertedAny = true;
                    appendedAtBoundary = true;
                }

                if (appendedAtBoundary) {
                    caretIndex = builder.Length;
                }

                if (sourceIndex < source.Length) {
                    builder.Append(source[sourceIndex]);
                }
            }

            if (!insertedAny) {
                return false;
            }

            result = builder.ToString();
            return !string.Equals(result, source, StringComparison.Ordinal);
        }

    }
}
