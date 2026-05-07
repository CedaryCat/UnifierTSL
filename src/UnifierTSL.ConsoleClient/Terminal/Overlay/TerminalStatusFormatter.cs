using UnifierTSL.Contracts.Display;
using UnifierTSL.Contracts.Terminal.Overlay;

namespace UnifierTSL.Terminal.Overlay
{
    internal static class TerminalStatusFormatter
    {
        public static StyledTextLine FormatFirstRow(
            Func<string, StyledTextLine[]> resolveLines,
            StatusBarTemplate? template,
            bool compact) {
            return FormatRows(resolveLines, template, compact).FirstOrDefault() ?? new StyledTextLine();
        }

        public static StyledTextLine[] FormatRows(
            Func<string, StyledTextLine[]> resolveLines,
            StatusBarTemplate? template,
            bool compact) {
            if (template is null) {
                return [];
            }

            List<StyledTextLine> rows = [];
            foreach (var row in template.Rows ?? []) {
                if (!ShouldRenderRow(row, compact)) {
                    continue;
                }

                var builder = new StyledTextLineBuilder();
                bool hasContent = false;
                foreach (var fieldTemplate in row.Fields ?? []) {
                    if (!TryGetValue(resolveLines, fieldTemplate, out var value)) {
                        continue;
                    }

                    if (hasContent && !string.IsNullOrEmpty(fieldTemplate.PrefixText)) {
                        builder.Append(fieldTemplate.PrefixText, fieldTemplate.StyleKey);
                    }

                    builder.Append(value);
                    hasContent = true;

                    if (!string.IsNullOrEmpty(fieldTemplate.SuffixText)) {
                        builder.Append(fieldTemplate.SuffixText, fieldTemplate.StyleKey);
                    }
                }

                var line = new StyledTextLine {
                    LineStyleId = row.StyleKey,
                    Runs = builder.Build().Runs,
                };
                if (StyledTextLineOps.HasVisibleText(line)) {
                    rows.Add(line);
                }
            }

            return [.. rows];
        }

        private static bool ShouldRenderRow(StatusBarRowTemplate row, bool compact) {
            return row.Visibility switch {
                StatusRowVisibility.Always => true,
                StatusRowVisibility.CompactOnly => compact,
                _ => !compact,
            };
        }

        private static bool TryGetValue(
            Func<string, StyledTextLine[]> resolveLines,
            StatusBarFieldTemplate fieldTemplate,
            out StyledTextLine value) {
            value = new StyledTextLine();

            StyledTextLine candidate = resolveLines(fieldTemplate.FieldKey).FirstOrDefault() ?? new StyledTextLine();
            if (!StyledTextLineOps.HasVisibleText(candidate)) {
                return !fieldTemplate.HideWhenEmpty;
            }

            value = ApplyFieldStyle(candidate, fieldTemplate.StyleKey);
            return true;
        }

        private static StyledTextLine ApplyFieldStyle(StyledTextLine candidate, string styleKey) {
            if (string.IsNullOrWhiteSpace(styleKey) || candidate.Runs.Length == 0) {
                return candidate;
            }

            return new StyledTextLine {
                LineStyleId = candidate.LineStyleId,
                Runs = [.. candidate.Runs.Select(run => new StyledTextRun {
                    Text = run.Text,
                    StyleId = string.IsNullOrWhiteSpace(run.StyleId) ? styleKey : run.StyleId,
                })],
            };
        }
    }
}
