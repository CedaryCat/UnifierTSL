namespace UnifierTSL.TextEditing {
    public static class LogicalLineBounds {
        public static int GetStartIndex(string? text, int index) {
            var value = text ?? string.Empty;
            if (value.Length == 0) {
                return 0;
            }

            var boundedIndex = NormalizeIndex(value, index);
            if (boundedIndex == 0) {
                return 0;
            }

            for (var i = boundedIndex - 1; i >= 0; i--) {
                if (value[i] == '\n') {
                    return i + 1;
                }

                if (value[i] == '\r') {
                    return i + (i + 1 < value.Length && value[i + 1] == '\n' ? 2 : 1);
                }
            }

            return 0;
        }

        public static int GetContentEndIndex(string? text, int index) {
            var value = text ?? string.Empty;
            if (value.Length == 0) {
                return 0;
            }

            var boundedIndex = NormalizeIndex(value, index);
            for (var i = boundedIndex; i < value.Length; i++) {
                if (value[i] is '\r' or '\n') {
                    return i;
                }
            }

            return value.Length;
        }

        public static int GetNextLineStartIndex(string? text, int index) {
            var value = text ?? string.Empty;
            if (value.Length == 0) {
                return 0;
            }

            var lineEndIndex = GetContentEndIndex(value, index);
            if (lineEndIndex >= value.Length) {
                return value.Length;
            }

            return value[lineEndIndex] == '\r' && lineEndIndex + 1 < value.Length && value[lineEndIndex + 1] == '\n'
                ? lineEndIndex + 2
                : lineEndIndex + 1;
        }

        public static int GetPreviousLineStartIndex(string? text, int index) {
            var value = text ?? string.Empty;
            if (value.Length == 0) {
                return 0;
            }

            var currentLineStart = GetStartIndex(value, index);
            if (currentLineStart == 0) {
                return 0;
            }

            var lineBreakStart = currentLineStart - 1;
            if (lineBreakStart > 0
                && value[lineBreakStart] == '\n'
                && value[lineBreakStart - 1] == '\r') {
                lineBreakStart -= 1;
            }

            return GetStartIndex(value, lineBreakStart);
        }

        public static bool IsCursorAtEnd(string? text, int cursorIndex) {
            var value = text ?? string.Empty;
            return NormalizeIndex(value, cursorIndex) == GetContentEndIndex(value, cursorIndex);
        }

        private static int NormalizeIndex(string text, int index) {
            var boundedIndex = Math.Clamp(index, 0, text.Length);
            return boundedIndex > 0
                && boundedIndex < text.Length
                && text[boundedIndex - 1] == '\r'
                && text[boundedIndex] == '\n'
                    ? boundedIndex - 1
                    : boundedIndex;
        }
    }
}
