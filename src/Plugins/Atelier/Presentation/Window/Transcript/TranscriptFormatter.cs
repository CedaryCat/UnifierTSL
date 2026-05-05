using Atelier.Presentation.Prompt;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting.Hosting;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using UnifierTSL.Contracts.Display;
using UnifierTSL.Surface.Prompting.Model;

namespace Atelier.Presentation.Window.Transcript
{
    internal sealed class TranscriptFormatter
    {
        private const int ErrorBoxMaxContentWidth = 96;
        private static readonly StyleDictionary Styles = StyleDictionaryOps.Merge(
            SurfaceStyleCatalog.Default,
            PromptStyles.Default);

        public StyleDictionary StyleDictionary => Styles;

        public string FormatReturnValueSummary(object? returnValue) {
            var text = FormatReturnValueText(returnValue)
                .Replace("\r\n", "\\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Replace("\n", "\\n", StringComparison.Ordinal);
            return text.Length <= 120 ? text : text[..117] + "...";
        }

        public ImmutableArray<StyledTextLine> FormatSubmittedInput(string? sourceText, IReadOnlyList<PromptHighlightSpan>? highlights = null) {
            var source = sourceText ?? string.Empty;
            IReadOnlyList<PromptHighlightSpan> resolvedHighlights = highlights is { Count: > 0 }
                ? [.. highlights
                    .Where(static span => span.Length > 0)
                    .OrderBy(static span => span.StartIndex)
                    .ThenBy(static span => span.Length)]
                : [];
            return FormatTextAsStyledLines(
                source,
                resolvedHighlights,
                PromptDefaults.Prompt,
                continuationPrefix: CreateContinuationPrefix(PromptDefaults.Prompt));
        }

        public ImmutableArray<StyledTextLine> FormatReturnValue(object? returnValue) {
            var formatted = FormatReturnValueText(returnValue);
            return FormatTextAsStyledLines(formatted, CreateReturnValueHighlights(formatted));
        }

        public ImmutableArray<StyledTextLine> FormatErrorBox(string title, IReadOnlyList<string> lines) {
            var normalizedTitle = string.IsNullOrWhiteSpace(title) ? "Error" : title.Trim();
            var wrappedLines = NormalizeErrorLines(lines);
            var titleWidth = GetDisplayWidth(normalizedTitle);
            var contentWidth = wrappedLines.Count == 0
                ? titleWidth
                : Math.Max(titleWidth, wrappedLines.Max(GetDisplayWidth));
            var innerWidth = Math.Max(contentWidth + 2, titleWidth + 2);
            contentWidth = innerWidth - 2;
            int fillerWidth = innerWidth - titleWidth;
            int leftBorderWidth = fillerWidth / 2;
            int rightBorderWidth = fillerWidth - leftBorderWidth;

            var builder = ImmutableArray.CreateBuilder<StyledTextLine>();
            builder.Add(FormatErrorLine($"┌{new string('─', leftBorderWidth)}{normalizedTitle}{new string('─', rightBorderWidth)}┐"));
            foreach (var line in wrappedLines) {
                var framedLine = $"│ {PadToDisplayWidth(line, contentWidth)} │";
                builder.Add(FormatErrorLine(framedLine, TryResolveDiagnosticSeverity(line)));
            }

            builder.Add(FormatErrorLine($"└{new string('─', innerWidth)}┘"));
            return builder.ToImmutable();
        }

        private static StyledTextLine FormatErrorLine(string text, DiagnosticSeverity? severity = null) {
            return ApplyWholeLineStyle(text, severity == DiagnosticSeverity.Warning
                ? SurfaceStyleCatalog.Warning
                : SurfaceStyleCatalog.Negative);
        }

        private static List<string> NormalizeErrorLines(IReadOnlyList<string> lines) {
            List<string> normalized = [];
            foreach (var rawLine in lines) {
                var source = (rawLine ?? string.Empty)
                    .Replace("\r\n", "\n", StringComparison.Ordinal)
                    .Replace('\r', '\n');
                foreach (var line in source.Split('\n')) {
                    if (line.Length == 0) {
                        normalized.Add(string.Empty);
                        continue;
                    }

                    normalized.AddRange(WrapToDisplayWidth(line, ErrorBoxMaxContentWidth));
                }
            }

            if (normalized.Count == 0) {
                normalized.Add(string.Empty);
            }

            return normalized;
        }

        private static DiagnosticSeverity? TryResolveDiagnosticSeverity(string line) {
            return line.StartsWith("warning ", StringComparison.OrdinalIgnoreCase)
                ? DiagnosticSeverity.Warning
                : line.StartsWith("error ", StringComparison.OrdinalIgnoreCase)
                    ? DiagnosticSeverity.Error
                    : null;
        }

        private static IEnumerable<string> WrapToDisplayWidth(string text, int maxWidth) {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxWidth);

            StringBuilder builder = new();
            int width = 0;
            foreach (var rune in text.EnumerateRunes()) {
                int runeWidth = GetDisplayWidth(rune);
                if (width > 0 && width + runeWidth > maxWidth) {
                    yield return builder.ToString();
                    builder.Clear();
                    width = 0;
                }

                builder.Append(rune.ToString());
                width += runeWidth;
            }

            if (builder.Length > 0 || text.Length == 0) {
                yield return builder.ToString();
            }
        }

        private static string PadToDisplayWidth(string text, int width) {
            var normalized = text ?? string.Empty;
            return normalized + new string(' ', Math.Max(0, width - GetDisplayWidth(normalized)));
        }

        private static int GetDisplayWidth(string text) {
            return string.IsNullOrEmpty(text)
                ? 0
                : text.EnumerateRunes().Sum(GetDisplayWidth);
        }

        // Transcript boxes need stable alignment with CJK compiler messages, so plain string.Length is not enough.
        private static int GetDisplayWidth(Rune rune) {
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark
                || Rune.IsControl(rune)) {
                return 0;
            }

            var value = rune.Value;
            return value switch {
                >= 0x1100 and <= 0x115F => 2,
                0x2329 or 0x232A => 2,
                >= 0x2E80 and <= 0xA4CF when value != 0x303F => 2,
                >= 0xAC00 and <= 0xD7A3 => 2,
                >= 0xF900 and <= 0xFAFF => 2,
                >= 0xFE10 and <= 0xFE19 => 2,
                >= 0xFE30 and <= 0xFE6F => 2,
                >= 0xFF00 and <= 0xFF60 => 2,
                >= 0xFFE0 and <= 0xFFE6 => 2,
                >= 0x1F300 and <= 0x1FAFF => 2,
                >= 0x20000 and <= 0x3FFFD => 2,
                _ => 1,
            };
        }

        private static string FormatReturnValueText(object? returnValue) {
            try {
                return CSharpObjectFormatter.Instance.FormatObject(returnValue);
            }
            catch {
                return returnValue switch {
                    null => "null",
                    _ => returnValue.ToString() ?? returnValue.GetType().Name,
                };
            }
        }

        private static ImmutableArray<PromptHighlightSpan> CreateReturnValueHighlights(string text) {
            var builder = ImmutableArray.CreateBuilder<PromptHighlightSpan>();
            foreach (var token in SyntaxFactory.ParseTokens(text)) {
                var styleId = ResolveReturnValueTokenStyle(token.Kind());
                if (!string.IsNullOrEmpty(styleId)) {
                    builder.Add(new PromptHighlightSpan(token.SpanStart, token.Span.Length, styleId));
                }
            }

            return builder.ToImmutable();
        }

        private static string ResolveReturnValueTokenStyle(SyntaxKind kind) {
            return kind switch {
                SyntaxKind.TrueKeyword or SyntaxKind.FalseKeyword or SyntaxKind.NullKeyword => PromptStyles.SyntaxKeyword,
                SyntaxKind.NumericLiteralToken => PromptStyles.SyntaxNumber,
                SyntaxKind.StringLiteralToken or SyntaxKind.CharacterLiteralToken => PromptStyles.SyntaxString,
                _ => string.Empty,
            };
        }

        private static ImmutableArray<StyledTextLine> FormatTextAsStyledLines(
            string text,
            IReadOnlyList<PromptHighlightSpan> highlights,
            string? prefix = null,
            string? continuationPrefix = null) {
            var builder = ImmutableArray.CreateBuilder<StyledTextLine>();
            foreach (var line in EnumerateLines(text)) {
                builder.Add(FormatStyledLine(
                    text,
                    line.Start,
                    line.Length,
                    highlights,
                    builder.Count == 0 ? prefix : continuationPrefix));
            }

            if (builder.Count == 0) {
                builder.Add(string.IsNullOrEmpty(prefix) ? new StyledTextLine() : ApplyWholeLineStyle(prefix, SurfaceStyleCatalog.Accent));
            }

            return builder.ToImmutable();
        }

        private static StyledTextLine FormatStyledLine(
            string source,
            int lineStart,
            int lineLength,
            IReadOnlyList<PromptHighlightSpan> highlights,
            string? prefix) {
            var lineEnd = lineStart + lineLength;
            var builder = new StyledTextLineBuilder();
            if (!string.IsNullOrEmpty(prefix)) {
                AppendStyled(builder, prefix, SurfaceStyleCatalog.Accent);
            }

            var cursor = lineStart;
            foreach (var span in highlights) {
                var start = Math.Max(lineStart, span.StartIndex);
                var end = Math.Min(lineEnd, span.StartIndex + Math.Max(0, span.Length));
                if (end <= start || start >= lineEnd) {
                    if (start >= lineEnd) {
                        break;
                    }

                    continue;
                }

                if (start < cursor) {
                    start = cursor;
                }

                if (start > cursor) {
                    AppendPlain(builder, source.AsSpan(cursor, start - cursor));
                }

                AppendStyled(builder, source.AsSpan(start, end - start), span.StyleId);
                cursor = end;
            }

            if (cursor < lineEnd) {
                AppendPlain(builder, source.AsSpan(cursor, lineEnd - cursor));
            }

            return builder.Build();
        }

        private static void AppendPlain(StyledTextLineBuilder builder, ReadOnlySpan<char> text) {
            if (text.IsEmpty) {
                return;
            }

            builder.Append(text.ToString());
        }

        private static void AppendStyled(StyledTextLineBuilder builder, ReadOnlySpan<char> text, string? styleId) {
            if (text.IsEmpty) {
                return;
            }

            builder.Append(text.ToString(), styleId ?? string.Empty);
        }

        private static void AppendStyled(StyledTextLineBuilder builder, string text, string? styleId) {
            AppendStyled(builder, text.AsSpan(), styleId);
        }

        private static StyledTextLine ApplyWholeLineStyle(string text, string styleId) {
            return new StyledTextLine {
                LineStyleId = styleId,
                Runs = string.IsNullOrEmpty(text)
                    ? []
                    : [
                        new StyledTextRun {
                            Text = text,
                        },
                    ],
            };
        }

        private static string CreateContinuationPrefix(string? prefix) {
            return string.IsNullOrEmpty(prefix)
                ? string.Empty
                : new string(' ', prefix.Length);
        }

        private static List<(int Start, int Length)> EnumerateLines(string text) {
            List<(int Start, int Length)> lines = [];
            if (text.Length == 0) {
                lines.Add((0, 0));
                return lines;
            }

            var lineStart = 0;
            for (var index = 0; index < text.Length; index++) {
                if (text[index] != '\r' && text[index] != '\n') {
                    continue;
                }

                lines.Add((lineStart, index - lineStart));
                if (text[index] == '\r' && index + 1 < text.Length && text[index + 1] == '\n') {
                    index++;
                }

                lineStart = index + 1;
            }

            lines.Add((lineStart, text.Length - lineStart));
            return lines;
        }
    }
}
