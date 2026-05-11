using System.Security.Cryptography;
using System.Text;

namespace UnifierTSL.Surface.Prompting.Model {
    public enum EmptySubmitBehavior : byte {
        KeepInput,
        AcceptGhostIfAvailable,
    }

    public enum PromptSubmitReadiness : byte {
        UseFallback,
        Ready,
        NotReady,
    }

    public readonly record struct PromptHighlightSpan(
        int StartIndex,
        int Length,
        string StyleId) {
        public int EndIndex => StartIndex + Length;
    }

    public sealed class PromptStyledText {
        public string Text { get; init; } = string.Empty;

        public PromptHighlightSpan[] Highlights { get; init; } = [];
    }

    public readonly record struct PromptTextEdit(
        int StartIndex,
        int Length,
        string NewText) {
        public string Apply(string sourceText) {
            string source = sourceText ?? string.Empty;
            int start = Math.Clamp(StartIndex, 0, source.Length);
            int length = Math.Clamp(Length, 0, source.Length - start);
            string replacement = NewText ?? string.Empty;
            return source[..start] + replacement + source[(start + length)..];
        }
    }

    public readonly record struct PromptEditTarget(
        int StartIndex,
        int Length,
        string RawText,
        bool Quoted,
        bool LeadingCharacterEscaped,
        bool HasLeadingQuote,
        bool HasTrailingQuote,
        bool AllowUnquotedWhitespace) {
        public int EndIndex => StartIndex + Length;
    }

    public sealed class PromptCompletionItem {
        public string Id { get; init; } = string.Empty;
        public string DisplayText { get; init; } = string.Empty;
        public string SecondaryDisplayText { get; init; } = string.Empty;
        public string TrailingDisplayText { get; init; } = string.Empty;
        public string SummaryText { get; init; } = string.Empty;
        public string DisplayStyleId { get; init; } = string.Empty;
        public string SecondaryDisplayStyleId { get; init; } = string.Empty;
        public string TrailingDisplayStyleId { get; init; } = string.Empty;
        public string SummaryStyleId { get; init; } = string.Empty;
        public string PreviewStyleId { get; init; } = string.Empty;
        public PromptHighlightSpan[] DisplayHighlights { get; init; } = [];
        public PromptHighlightSpan[] SecondaryDisplayHighlights { get; init; } = [];
        public PromptHighlightSpan[] TrailingDisplayHighlights { get; init; } = [];
        public PromptHighlightSpan[] SummaryHighlights { get; init; } = [];

        public PromptTextEdit PrimaryEdit { get; init; }

        public int Weight { get; init; }

        public static string CreateId(string? scope, params string?[] parts) {
            var builder = new StringBuilder();
            foreach (var part in parts ?? []) {
                var value = part ?? string.Empty;
                builder.Append(value.Length);
                builder.Append(':');
                builder.Append(value);
                builder.Append(';');
            }

            var prefix = string.IsNullOrWhiteSpace(scope)
                ? "completion"
                : scope.Trim();
            return prefix + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
        }
    }

    public sealed class PromptInputState {
        public string InputText { get; set; } = string.Empty;

        public int CursorIndex { get; set; }

        public int CompletionIndex { get; set; }

        public int CompletionCount { get; set; }

        public int CandidateWindowOffset { get; set; }

        public string PreferredCompletionText { get; set; } = string.Empty;

        public string PreferredInterpretationId { get; set; } = string.Empty;

        public PromptInputState Normalize(
            bool clampCursorToText = true,
            bool trimPreferredInterpretationId = false,
            bool normalizePreferredCompletionText = true) {
            InputText ??= string.Empty;
            CursorIndex = clampCursorToText
                ? Math.Clamp(CursorIndex, 0, InputText.Length)
                : Math.Max(0, CursorIndex);
            CompletionIndex = Math.Max(0, CompletionIndex);
            CompletionCount = Math.Max(0, CompletionCount);
            CandidateWindowOffset = Math.Max(0, CandidateWindowOffset);
            if (normalizePreferredCompletionText) {
                PreferredCompletionText ??= string.Empty;
            }

            PreferredInterpretationId = trimPreferredInterpretationId
                ? PreferredInterpretationId?.Trim() ?? string.Empty
                : PreferredInterpretationId ?? string.Empty;
            return this;
        }

        public PromptInputState CopyNormalized(
            bool clampCursorToText = true,
            bool trimPreferredInterpretationId = false,
            bool normalizePreferredCompletionText = true) {
            return new PromptInputState {
                InputText = InputText,
                CursorIndex = CursorIndex,
                CompletionIndex = CompletionIndex,
                CompletionCount = CompletionCount,
                CandidateWindowOffset = CandidateWindowOffset,
                PreferredCompletionText = PreferredCompletionText,
                PreferredInterpretationId = PreferredInterpretationId,
            }.Normalize(
                clampCursorToText,
                trimPreferredInterpretationId,
                normalizePreferredCompletionText);
        }

        public bool ContentEquals(PromptInputState? other) {
            return ReferenceEquals(this, other)
                || other is not null
                && string.Equals(InputText, other.InputText, StringComparison.Ordinal)
                && CursorIndex == other.CursorIndex
                && CompletionIndex == other.CompletionIndex
                && CompletionCount == other.CompletionCount
                && CandidateWindowOffset == other.CandidateWindowOffset
                && string.Equals(PreferredCompletionText, other.PreferredCompletionText, StringComparison.Ordinal)
                && string.Equals(PreferredInterpretationId, other.PreferredInterpretationId, StringComparison.Ordinal);
        }
    }
}
