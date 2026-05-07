using UnifierTSL.Contracts.Display;

namespace UnifierTSL.Terminal.Shell
{
    internal static class TerminalTextLayoutOps
    {
        internal static PopupContentLine FitPopupContentLine(PopupContentLine line, int maxWidth) {
            if (maxWidth <= 0 || string.IsNullOrEmpty(line.Text)) {
                return line with {
                    Text = string.Empty,
                    StyleSlots = [],
                    BackgroundStyleSlots = [],
                };
            }

            var normalizedStyles = NormalizePopupStyleSlots(line.Text, line.StyleSlots);
            var normalizedBackgrounds = NormalizePopupStyleSlots(line.Text, line.BackgroundStyleSlots);
            var inputWidth = TerminalCellWidth.Measure(line.Text);
            if (inputWidth <= maxWidth) {
                return line with {
                    StyleSlots = normalizedStyles,
                    BackgroundStyleSlots = normalizedBackgrounds,
                };
            }

            if (maxWidth <= 3) {
                var visibleLength = TerminalCellWidth.TakeLengthByCols(line.Text, 0, maxWidth, out _);
                if (visibleLength <= 0) {
                    return line with {
                        Text = string.Empty,
                        StyleSlots = [],
                        BackgroundStyleSlots = [],
                    };
                }

                return line with {
                    Text = line.Text[..visibleLength],
                    StyleSlots = normalizedStyles[..visibleLength],
                    BackgroundStyleSlots = normalizedBackgrounds[..visibleLength],
                };
            }

            var contentLength = TerminalCellWidth.TakeLengthByCols(line.Text, 0, maxWidth - 3, out _);
            var contentText = contentLength > 0
                ? line.Text[..contentLength]
                : string.Empty;
            ushort[] contentStyles = contentLength > 0
                ? normalizedStyles[..contentLength]
                : [];
            ushort[] contentBackgrounds = contentLength > 0
                ? normalizedBackgrounds[..contentLength]
                : [];
            return line with {
                Text = contentText + "...",
                StyleSlots = [.. contentStyles, 0, 0, 0],
                BackgroundStyleSlots = [.. contentBackgrounds, 0, 0, 0],
            };
        }

        internal static PopupContentLine PadPopupContentLine(
            PopupContentLine line,
            int targetWidth,
            ushort paddingStyleSlot = 0,
            ushort paddingBackgroundStyleSlot = 0) {
            var width = TerminalCellWidth.Measure(line.Text);
            if (width >= targetWidth) {
                return line with {
                    StyleSlots = NormalizePopupStyleSlots(line.Text, line.StyleSlots),
                    BackgroundStyleSlots = NormalizePopupStyleSlots(line.Text, line.BackgroundStyleSlots),
                };
            }

            var padding = Math.Max(0, targetWidth - width);
            var text = line.Text + new string(' ', padding);
            var styles = NormalizePopupStyleSlots(line.Text, line.StyleSlots);
            var backgrounds = NormalizePopupStyleSlots(line.Text, line.BackgroundStyleSlots);
            if (padding <= 0) {
                return line with {
                    Text = text,
                    StyleSlots = styles,
                    BackgroundStyleSlots = backgrounds,
                };
            }

            return line with {
                Text = text,
                StyleSlots = [.. styles, .. Enumerable.Repeat(paddingStyleSlot, padding)],
                BackgroundStyleSlots = [.. backgrounds, .. Enumerable.Repeat(paddingBackgroundStyleSlot, padding)],
            };
        }

        internal static ushort[] NormalizePopupStyleSlots(string text, ushort[]? styleSlots) {
            if (string.IsNullOrEmpty(text)) {
                return [];
            }

            styleSlots ??= [];
            if (styleSlots.Length == text.Length) {
                return styleSlots;
            }

            if (styleSlots.Length == 0) {
                return new ushort[text.Length];
            }

            var normalized = new ushort[text.Length];
            Array.Copy(styleSlots, normalized, Math.Min(styleSlots.Length, text.Length));
            return normalized;
        }

        internal static ushort[] BuildStatusStyleSlots(IReadOnlyList<StyledTextRun> runs, StyleDictionary styleDictionary) {
            List<ushort> styleSlots = [];
            foreach (var run in runs) {
                if (string.IsNullOrEmpty(run.Text)) {
                    continue;
                }

                var slot = ResolveStyleSlot(styleDictionary, run.StyleId);
                for (var index = 0; index < run.Text.Length; index++) {
                    styleSlots.Add(slot);
                }
            }

            return [.. styleSlots];
        }

        internal static ushort ResolveStyleSlot(StyleDictionary styleDictionary, string? styleId) {
            return StyleDictionaryOps.Resolve(styleDictionary, styleId)?.Slot ?? 0;
        }

        internal static StyledTextRun[] FitStyledTextRuns(StyledTextLine line, int maxWidth) {
            if (maxWidth <= 0 || line.Runs.Length == 0) {
                return [];
            }

            var inputWidth = MeasureStyledTextRuns(line.Runs);
            if (inputWidth <= maxWidth) {
                return [.. line.Runs.Where(static run => !string.IsNullOrEmpty(run.Text))];
            }

            if (maxWidth <= 3) {
                return TakeStyledTextRunsByWidth(line.Runs, maxWidth);
            }

            List<StyledTextRun> fitted = [.. TakeStyledTextRunsByWidth(line.Runs, maxWidth - 3)];
            if (fitted.Count > 0 && string.IsNullOrEmpty(fitted[^1].StyleId)) {
                var previous = fitted[^1];
                fitted[^1] = new StyledTextRun {
                    Text = previous.Text + "...",
                    StyleId = previous.StyleId,
                };
            }
            else {
                fitted.Add(new StyledTextRun {
                    Text = "...",
                });
            }

            return [.. fitted];
        }

        internal static StyledTextRun[] TakeStyledTextRunsByWidth(IReadOnlyList<StyledTextRun> runs, int maxWidth) {
            if (maxWidth <= 0) {
                return [];
            }

            List<StyledTextRun> fitted = [];
            var remainingWidth = maxWidth;
            foreach (var run in runs) {
                if (remainingWidth <= 0 || string.IsNullOrEmpty(run.Text)) {
                    break;
                }

                var visibleLength = TerminalCellWidth.TakeLengthByCols(run.Text, 0, remainingWidth, out _);
                if (visibleLength <= 0) {
                    continue;
                }

                var visible = run.Text[..visibleLength];
                fitted.Add(new StyledTextRun {
                    Text = visible,
                    StyleId = run.StyleId,
                });
                remainingWidth -= TerminalCellWidth.Measure(visible);
                if (visibleLength < run.Text.Length) {
                    break;
                }
            }

            return [.. fitted];
        }

        internal static int MeasureStyledTextRuns(IReadOnlyList<StyledTextRun> runs) {
            var width = 0;
            foreach (var run in runs) {
                if (!string.IsNullOrEmpty(run.Text)) {
                    width += TerminalCellWidth.Measure(run.Text);
                }
            }

            return width;
        }

        internal static string FitText(string input, int maxWidth) {
            if (maxWidth <= 0 || string.IsNullOrEmpty(input)) {
                return string.Empty;
            }

            var inputWidth = TerminalCellWidth.Measure(input);
            if (inputWidth <= maxWidth) {
                return input;
            }

            if (maxWidth <= 3) {
                var visibleLength = TerminalCellWidth.TakeLengthByCols(input, 0, maxWidth, out _);
                return visibleLength > 0 ? input[..visibleLength] : string.Empty;
            }

            var contentLength = TerminalCellWidth.TakeLengthByCols(input, 0, maxWidth - 3, out _);
            var visible = contentLength > 0 ? input[..contentLength] : string.Empty;
            return visible + "...";
        }
    }
}
