using System.Text;
using System.Diagnostics.CodeAnalysis;
using MemoryPack;

namespace UnifierTSL.Contracts.Display {
    [Flags]
    public enum StyledTextAttributes : byte {
        None = 0,
        Underline = 1 << 0,
    }

    [MemoryPackable]
    public sealed partial class StyleDictionary {
        public StyledTextStyle[] Styles { get; init; } = [];
    }

    [MemoryPackable]
    public sealed partial class StyledColorValue {
        public byte Red { get; init; }
        public byte Green { get; init; }
        public byte Blue { get; init; }
    }

    [MemoryPackable]
    public sealed partial class StyledTextStyle {
        public string StyleId { get; init; } = string.Empty;
        public ushort Slot { get; init; }
        public StyledColorValue? Foreground { get; init; }
        public StyledColorValue? Background { get; init; }
        public StyledTextAttributes TextAttributes { get; init; }
    }

    public static class SurfaceStyleCatalog {
        public const long Version = 1;
        public const string Accent = "accent";
        public const string Positive = "positive";
        public const string Warning = "warning";
        public const string Negative = "negative";
        public const string PromptLabel = "prompt.label";
        public const string PromptInput = "prompt.input";
        public const string PromptCompletionBadge = "prompt.completion.badge";
        public const string InlineHint = "hint.inline";
        public const string CompletionPopupBorder = "completion.popup.border";
        public const string CompletionPopupText = "completion.popup.text";
        public const string CompletionPopupDetail = "completion.popup.detail";
        public const string CompletionItemSelected = "completion.item.selected";
        public const string CompletionItemSelectedMarker = "completion.item.selected.marker";
        public const string StatusBand = "status.band";
        public const string StatusTitle = "status.title";
        public const string StatusSummary = "status.summary";
        public const string StatusDetail = "status.detail";
        public const string StatusHeaderIndicator = "status.header.indicator";
        public const string StatusHeaderPositive = "status.header.positive";
        public const string StatusHeaderWarning = "status.header.warning";
        public const string StatusHeaderNegative = "status.header.negative";
        public const string SelectionCurrent = "selection.current";

        public static StyleDictionary Default { get; } = new() {
            Styles = [
                new StyledTextStyle {
                    StyleId = Accent,
                    Foreground = Rgb(255, 255, 255),
                },
                new StyledTextStyle {
                    StyleId = Positive,
                    Foreground = Rgb(0, 192, 96),
                },
                new StyledTextStyle {
                    StyleId = Warning,
                    Foreground = Rgb(224, 192, 0),
                },
                new StyledTextStyle {
                    StyleId = Negative,
                    Foreground = Rgb(224, 80, 80),
                },
                new StyledTextStyle {
                    StyleId = InlineHint,
                    Foreground = Rgb(128, 128, 128),
                },
                new StyledTextStyle {
                    StyleId = CompletionPopupBorder,
                    Foreground = Rgb(192, 192, 192),
                },
                new StyledTextStyle {
                    StyleId = CompletionPopupText,
                    Foreground = Rgb(192, 192, 192),
                },
                new StyledTextStyle {
                    StyleId = CompletionPopupDetail,
                    Foreground = Rgb(192, 192, 192),
                    Background = Rgb(40, 40, 40),
                },
                new StyledTextStyle {
                    StyleId = CompletionItemSelected,
                    Foreground = Rgb(220, 220, 220),
                    Background = Rgb(60, 60, 60),
                },
                new StyledTextStyle {
                    StyleId = CompletionItemSelectedMarker,
                    Foreground = Rgb(86, 156, 214),
                },
                new StyledTextStyle {
                    StyleId = StatusHeaderIndicator,
                    Foreground = Rgb(255, 255, 255),
                    Background = Rgb(0, 128, 0),
                },
                new StyledTextStyle {
                    StyleId = StatusHeaderPositive,
                    Foreground = Rgb(255, 255, 255),
                    Background = Rgb(0, 128, 0),
                },
                new StyledTextStyle {
                    StyleId = StatusHeaderWarning,
                    Foreground = Rgb(255, 255, 255),
                    Background = Rgb(255, 135, 0),
                },
                new StyledTextStyle {
                    StyleId = StatusHeaderNegative,
                    Foreground = Rgb(255, 255, 255),
                    Background = Rgb(255, 0, 0),
                },
                new StyledTextStyle {
                    StyleId = SelectionCurrent,
                    Foreground = Rgb(255, 255, 255),
                },
            ],
        };

        private static StyledColorValue Rgb(byte red, byte green, byte blue) {
            return new StyledColorValue {
                Red = red,
                Green = green,
                Blue = blue,
            };
        }
    }

    [MemoryPackable]
    public sealed partial class StyledTextRun {
        public string Text { get; init; } = string.Empty;
        public string StyleId { get; init; } = string.Empty;
    }

    [MemoryPackable]
    public sealed partial class StyledTextLine {
        public string LineStyleId { get; init; } = string.Empty;
        public StyledTextRun[] Runs { get; init; } = [];
    }

    public sealed class StyledTextLineBuilder {
        private readonly List<StyledTextRun> runs = [];

        public void Append(string text, string styleId = "") {
            if (string.IsNullOrEmpty(text)) {
                return;
            }

            if (runs.Count > 0 && string.Equals(runs[^1].StyleId, styleId, StringComparison.Ordinal)) {
                var previous = runs[^1];
                runs[^1] = new StyledTextRun {
                    Text = previous.Text + text,
                    StyleId = previous.StyleId,
                };
                return;
            }

            runs.Add(new StyledTextRun {
                Text = text,
                StyleId = styleId,
            });
        }

        public void Append(StyledTextLine? line) {
            if (line is null || line.Runs.Length == 0) {
                return;
            }

            foreach (var run in line.Runs) {
                Append(run.Text, run.StyleId);
            }
        }

        public StyledTextLine Build() {
            return new StyledTextLine {
                Runs = [.. runs],
            };
        }
    }

    public static class StyleDictionaryOps {
        public static StyledTextStyle? Resolve(StyleDictionary? dictionary, string? styleId) {
            if (dictionary is null || string.IsNullOrWhiteSpace(styleId) || dictionary.Styles.Length == 0) {
                return null;
            }

            return dictionary.Styles.FirstOrDefault(style =>
                string.Equals(style.StyleId, styleId, StringComparison.Ordinal));
        }

        public static StyledTextStyle? Resolve(StyleDictionary? dictionary, ushort slot) {
            if (dictionary is null || slot == 0 || dictionary.Styles.Length == 0) {
                return null;
            }

            return dictionary.Styles.FirstOrDefault(style => style.Slot == slot);
        }

        public static StyleDictionary Merge(params StyleDictionary?[] dictionaries) {
            if (dictionaries is null || dictionaries.Length == 0) {
                return new StyleDictionary();
            }

            Dictionary<string, StyledTextStyle> merged = new(StringComparer.Ordinal);
            foreach (var dictionary in dictionaries) {
                if (dictionary?.Styles is not { Length: > 0 } styles) {
                    continue;
                }

                foreach (var style in styles) {
                    if (style is null || string.IsNullOrWhiteSpace(style.StyleId)) {
                        continue;
                    }

                    merged[style.StyleId] = style;
                }
            }

            return new StyleDictionary {
                Styles = [.. merged.Values],
            };
        }

        public static bool ContentEquals(StyleDictionary? left, StyleDictionary? right) {
            return ReferenceEquals(left, right)
                || left is not null
                && right is not null
                && SequenceEqual(left.Styles, right.Styles, ContentEquals);
        }

        public static bool HasStyles([NotNullWhen(true)] StyleDictionary? dictionary) {
            return dictionary is { Styles.Length: > 0 };
        }

        private static bool ContentEquals(StyledTextStyle? left, StyledTextStyle? right) {
            return ReferenceEquals(left, right)
                || left is not null
                && right is not null
                && string.Equals(left.StyleId, right.StyleId, StringComparison.Ordinal)
                && left.Slot == right.Slot
                && ContentEquals(left.Foreground, right.Foreground)
                && ContentEquals(left.Background, right.Background)
                && left.TextAttributes == right.TextAttributes;
        }

        private static bool ContentEquals(StyledColorValue? left, StyledColorValue? right) {
            return ReferenceEquals(left, right)
                || left is not null
                && right is not null
                && left.Red == right.Red
                && left.Green == right.Green
                && left.Blue == right.Blue;
        }

        private static bool SequenceEqual<T>(IReadOnlyList<T>? left, IReadOnlyList<T>? right, Func<T, T, bool> elementEquals) {
            if (ReferenceEquals(left, right)) {
                return true;
            }

            if (left is null || right is null || left.Count != right.Count) {
                return false;
            }

            for (var index = 0; index < left.Count; index++) {
                if (!elementEquals(left[index], right[index])) {
                    return false;
                }
            }

            return true;
        }
    }

    public static class StyledTextLineOps {
        public static bool HasVisibleText(StyledTextLine? line) {
            return line is not null
                && line.Runs.Any(static run => !string.IsNullOrWhiteSpace(run.Text));
        }

        public static string ToPlainText(StyledTextLine? line) {
            if (line is null || line.Runs.Length == 0) {
                return string.Empty;
            }

            var builder = new StringBuilder();
            foreach (var run in line.Runs) {
                if (!string.IsNullOrEmpty(run.Text)) {
                    builder.Append(run.Text);
                }
            }

            return builder.ToString();
        }

        public static bool ContentEquals(StyledTextLine? left, StyledTextLine? right) {
            return string.Equals(BuildSignature(left), BuildSignature(right), StringComparison.Ordinal);
        }

        public static bool SequenceEqual(IReadOnlyList<StyledTextLine>? left, IReadOnlyList<StyledTextLine>? right) {
            if (ReferenceEquals(left, right)) {
                return true;
            }

            if (left is null || right is null || left.Count != right.Count) {
                return false;
            }

            for (var index = 0; index < left.Count; index++) {
                if (!ContentEquals(left[index], right[index])) {
                    return false;
                }
            }

            return true;
        }

        public static string BuildSignature(StyledTextLine? line) {
            if (line is null) {
                return string.Empty;
            }

            var builder = new StringBuilder()
                .Append(line.LineStyleId ?? string.Empty)
                .Append('\u001c');
            var hasRun = false;
            foreach (var run in line.Runs) {
                if (hasRun) {
                    builder.Append('\u001e');
                }

                builder
                    .Append(run.StyleId ?? string.Empty)
                    .Append('\u001d')
                    .Append(run.Text ?? string.Empty);
                hasRun = true;
            }

            return builder.ToString();
        }
    }
}
