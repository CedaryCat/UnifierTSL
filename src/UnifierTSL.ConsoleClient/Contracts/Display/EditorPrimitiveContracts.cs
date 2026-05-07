namespace UnifierTSL.Contracts.Display {
    public sealed class HighlightSpan {
        public int StartIndex { get; init; }
        public int Length { get; init; }
        public string StyleId { get; init; } = string.Empty;

        public int EndIndex => StartIndex + Length;
    }

    public sealed class InlineSegments {
        public string Text { get; init; } = string.Empty;
        public HighlightSpan[] Highlights { get; init; } = [];
    }

    public sealed class TextEditOperation {
        public int StartIndex { get; init; }
        public int Length { get; init; }
        public string NewText { get; init; } = string.Empty;

        public int EndIndex => StartIndex + Length;

        public string Apply(string? sourceText) {
            return Apply(sourceText, out _);
        }

        public string Apply(string? sourceText, out int caretIndex) {
            var source = sourceText ?? string.Empty;
            int start = Math.Clamp(StartIndex, 0, source.Length);
            int length = Math.Clamp(Length, 0, source.Length - start);
            string replacement = NewText ?? string.Empty;
            caretIndex = start + replacement.Length;
            return source[..start] + replacement + source[(start + length)..];
        }
    }

    public enum EmptyInputSubmitAction : byte {
        KeepInput,
        AcceptPreviewIfAvailable,
    }

    public enum SubmitReadiness : byte {
        UseFallback,
        Ready,
        NotReady,
    }

    public sealed class EditorSubmitBehavior {
        public EmptyInputSubmitAction EmptyInputAction { get; init; }
        public bool CtrlEnterBypassesPreview { get; init; } = true;
        public SubmitReadiness PlainEnterReadiness { get; init; } = SubmitReadiness.UseFallback;
    }

    public sealed class InlineInterpretationOption {
        public string Id { get; init; } = string.Empty;
        public InlineSegments Label { get; init; } = new();
    }

    public enum CompletionActivationMode : byte {
        Manual,
        Automatic,
    }

    public sealed class CompletionItem {
        public string Id { get; init; } = string.Empty;
        public InlineSegments Label { get; init; } = new();
        public InlineSegments SecondaryLabel { get; init; } = new();
        public InlineSegments TrailingLabel { get; init; } = new();
        public InlineSegments Summary { get; init; } = new();
        public TextEditOperation PrimaryEdit { get; init; } = new();
    }

    public static class InlineSegmentsOps {
        public static bool HasVisibleText(InlineSegments? value) {
            return value is not null && !string.IsNullOrWhiteSpace(value.Text);
        }

        public static StyledTextLine ToStyledTextLine(InlineSegments? value) {
            if (value is null || string.IsNullOrEmpty(value.Text)) {
                return new StyledTextLine();
            }

            HighlightSpan[] highlights = value.Highlights ?? [];
            if (highlights.Length == 0) {
                return new StyledTextLine {
                    Runs = [
                        new StyledTextRun {
                            Text = value.Text,
                        },
                    ],
                };
            }

            List<HighlightSpan> ordered = [.. highlights
                .Where(static span => span is not null && span.Length > 0)
                .OrderBy(static span => span.StartIndex)];
            StyledTextLineBuilder builder = new();
            int cursor = 0;
            foreach (HighlightSpan span in ordered) {
                int start = Math.Clamp(span.StartIndex, 0, value.Text.Length);
                int end = Math.Clamp(span.EndIndex, start, value.Text.Length);
                if (start > cursor) {
                    builder.Append(value.Text[cursor..start]);
                }

                if (end > start) {
                    builder.Append(value.Text[start..end], span.StyleId);
                }

                cursor = Math.Max(cursor, end);
            }

            if (cursor < value.Text.Length) {
                builder.Append(value.Text[cursor..]);
            }

            return builder.Build();
        }
    }
}
