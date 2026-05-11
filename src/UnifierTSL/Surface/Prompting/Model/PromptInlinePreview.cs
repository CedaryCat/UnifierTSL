namespace UnifierTSL.Surface.Prompting.Model {
    public readonly record struct PromptInlinePreviewInsertion(
        int SourceIndex,
        string Text,
        string StyleId);

    public static class PromptInlinePreview {
        public static bool TryCreateInsertions(
            string? currentText,
            PromptCompletionItem? candidate,
            out PromptInlinePreviewInsertion[] insertions) {
            insertions = [];

            if (candidate is null) {
                return false;
            }

            string source = currentText ?? string.Empty;
            PromptTextEdit edit = candidate.PrimaryEdit;
            int start = Math.Clamp(edit.StartIndex, 0, source.Length);
            int length = Math.Clamp(edit.Length, 0, source.Length - start);
            string currentSegment = source.Substring(start, length);
            return TryCreateInsertionsForSegment(
                currentSegment,
                edit.NewText ?? string.Empty,
                start,
                candidate.PreviewStyleId,
                out insertions);
        }

        private static bool TryCreateInsertionsForSegment(
            string currentSegment,
            string replacement,
            int baseSourceIndex,
            string styleId,
            out PromptInlinePreviewInsertion[] insertions) {
            insertions = [];

            if (string.IsNullOrEmpty(replacement)) {
                return false;
            }

            if (string.IsNullOrEmpty(currentSegment)) {
                insertions = [new PromptInlinePreviewInsertion(baseSourceIndex, replacement, styleId ?? string.Empty)];
                return true;
            }

            if (replacement.Length <= currentSegment.Length) {
                return false;
            }

            List<PromptInlinePreviewInsertion> results = [];
            System.Text.StringBuilder pending = new();
            int sourceIndex = 0;
            bool matchedAnyTypedChar = false;

            void FlushPendingInsertion() {
                if (pending.Length == 0) {
                    return;
                }

                results.Add(new PromptInlinePreviewInsertion(baseSourceIndex + sourceIndex, pending.ToString(), styleId ?? string.Empty));
                pending.Clear();
            }

            foreach (char candidateChar in replacement) {
                if (sourceIndex < currentSegment.Length
                    && char.ToUpperInvariant(candidateChar) == char.ToUpperInvariant(currentSegment[sourceIndex])) {
                    FlushPendingInsertion();
                    sourceIndex += 1;
                    matchedAnyTypedChar = true;
                    continue;
                }

                if (!matchedAnyTypedChar && !IsAllowedLeadingCharacter(candidateChar)) {
                    return false;
                }

                pending.Append(candidateChar);
            }

            if (sourceIndex != currentSegment.Length) {
                return false;
            }

            FlushPendingInsertion();
            if (results.Count == 0) {
                return false;
            }

            insertions = [.. results];
            return true;
        }

        private static bool IsAllowedLeadingCharacter(char value) {
            return value is '"' or '\\';
        }
    }
}
