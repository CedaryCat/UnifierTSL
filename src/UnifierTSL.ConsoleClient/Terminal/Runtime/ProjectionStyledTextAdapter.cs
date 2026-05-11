using UnifierTSL.Contracts.Display;
using UnifierTSL.Contracts.Projection;

namespace UnifierTSL.Terminal.Runtime {
    internal static class ProjectionStyledTextAdapter {
        public static StyledTextLine ToStyledFirstLine(ProjectionTextBlock? block) {
            return ToStyledLines(block).FirstOrDefault() ?? new StyledTextLine();
        }

        public static StyledTextLine[] ToStyledLines(ProjectionTextBlock? block) {
            return block?.Lines is not { Length: > 0 } lines
                ? []
                : [.. lines.Select(ToStyledLine)];
        }

        public static StyledTextLine[] ToStyledLines(IReadOnlyList<ProjectionTextBlock>? blocks) {
            return blocks is not { Count: > 0 }
                ? []
                : [.. blocks.SelectMany(ToStyledLines)];
        }

        public static InlineSegments ToInlineSegments(ProjectionTextBlock? block) {
            if (block?.Lines is not { Length: > 0 } lines) {
                return new InlineSegments();
            }

            List<HighlightSpan> highlights = [];
            System.Text.StringBuilder builder = new();
            for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++) {
                if (lineIndex > 0) {
                    builder.Append('\n');
                }

                foreach (var span in lines[lineIndex].Spans ?? []) {
                    var text = span.Text ?? string.Empty;
                    if (text.Length == 0) {
                        continue;
                    }

                    var startIndex = builder.Length;
                    builder.Append(text);
                    if (span.Style is { } style && !string.IsNullOrWhiteSpace(style.Key)) {
                        highlights.Add(new HighlightSpan {
                            StartIndex = startIndex,
                            Length = text.Length,
                            StyleId = style.Key,
                        });
                    }
                }
            }

            return new InlineSegments {
                Text = builder.ToString(),
                Highlights = [.. highlights],
            };
        }

        public static InlineSegments[] ToInlineSegments(IReadOnlyList<ProjectionTextBlock>? blocks) {
            return blocks is not { Count: > 0 }
                ? []
                : [.. blocks.Select(ToInlineSegments)];
        }

        private static StyledTextLine ToStyledLine(ProjectionTextLine line) {
            return new StyledTextLine {
                LineStyleId = line.Style?.Key ?? string.Empty,
                Runs = [.. (line.Spans ?? [])
                    .Where(static span => !string.IsNullOrEmpty(span.Text))
                    .Select(span => new StyledTextRun {
                        Text = span.Text ?? string.Empty,
                        StyleId = span.Style?.Key ?? string.Empty,
                    })],
            };
        }
    }
}
