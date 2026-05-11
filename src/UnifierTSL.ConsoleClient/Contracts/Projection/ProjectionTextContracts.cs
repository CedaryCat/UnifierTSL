using System;
using MemoryPack;

namespace UnifierTSL.Contracts.Projection {
    public static class ProjectionStyleSlots {
        public const ushort None = 0;
    }

    [Flags]
    public enum ProjectionTextAttributes : byte {
        None = 0,
        Underline = 1 << 0,
    }

    [MemoryPackable]
    public sealed partial class ProjectionStyleDictionary {
        public ProjectionStyleDefinition[] Styles { get; init; } = [];
    }

    [MemoryPackable]
    public sealed partial class ProjectionStyleDefinition {
        public string Key { get; init; } = string.Empty;
        public ushort Slot { get; init; }
        public ProjectionColorValue? Foreground { get; init; }
        public ProjectionColorValue? Background { get; init; }
        public ProjectionTextAttributes TextAttributes { get; init; }

        public override string ToString() {
            return string.IsNullOrWhiteSpace(Key) ? "<style>" : Key;
        }
    }

    [MemoryPackable]
    public sealed partial class ProjectionColorValue {
        public byte Red { get; init; }
        public byte Green { get; init; }
        public byte Blue { get; init; }
    }

    [MemoryPackable]
    public sealed partial class ProjectionTextBlock {
        public ProjectionTextLine[] Lines { get; init; } = [];
    }

    [MemoryPackable]
    public sealed partial class ProjectionTextAnimation {
        public int FrameStepTicks { get; init; }
        public ProjectionTextBlock[] Frames { get; init; } = [];
    }

    [MemoryPackable]
    public sealed partial class ProjectionTextLine {
        public ProjectionStyleDefinition? Style { get; init; }
        public ProjectionTextSpan[] Spans { get; init; } = [];
    }

    [MemoryPackable]
    public sealed partial class ProjectionTextSpan {
        public string Text { get; init; } = string.Empty;
        public ProjectionStyleDefinition? Style { get; init; }
    }

    public static class ProjectionStyleDictionaryOps {
        public static ProjectionStyleDictionary WithSlots(ProjectionStyleDictionary? dictionary) {
            return WithSlots(dictionary?.Styles);
        }

        public static ProjectionStyleDictionary WithSlots(IReadOnlyList<ProjectionStyleDefinition>? styles) {
            if (styles is not { Count: > 0 }) {
                return new ProjectionStyleDictionary();
            }

            Dictionary<string, ProjectionStyleDefinition> stylesByKey = new(StringComparer.Ordinal);
            foreach (var style in styles) {
                if (style is null || string.IsNullOrWhiteSpace(style.Key)) {
                    continue;
                }

                stylesByKey[style.Key] = style;
            }

            if (stylesByKey.Count > ushort.MaxValue) {
                throw new InvalidOperationException($"Projection style dictionary exceeds the {ushort.MaxValue} style slot limit.");
            }

            var slot = 1;
            return new ProjectionStyleDictionary {
                Styles = [.. stylesByKey.Values.Select(style => new ProjectionStyleDefinition {
                    Key = style.Key,
                    Slot = checked((ushort)slot++),
                    Foreground = CloneColor(style.Foreground),
                    Background = CloneColor(style.Background),
                    TextAttributes = style.TextAttributes,
                })],
            };
        }

        public static ProjectionStyleDefinition? Resolve(ProjectionStyleDictionary? dictionary, string? key) {
            if (dictionary?.Styles is not { Length: > 0 } styles || string.IsNullOrWhiteSpace(key)) {
                return null;
            }

            return styles.FirstOrDefault(style => string.Equals(style.Key, key, StringComparison.Ordinal));
        }

        public static ProjectionStyleDefinition? Reference(string? key) {
            return string.IsNullOrWhiteSpace(key)
                ? null
                : new ProjectionStyleDefinition {
                    Key = key,
                };
        }

        public static string BuildSignature(ProjectionStyleDictionary? dictionary) {
            if (dictionary?.Styles is not { Length: > 0 } styles) {
                return string.Empty;
            }

            var builder = new System.Text.StringBuilder();
            foreach (var style in styles.Where(static style => style is not null && !string.IsNullOrWhiteSpace(style.Key))) {
                builder
                    .Append(style.Key)
                    .Append('\u001d')
                    .Append(style.Foreground?.Red).Append(',').Append(style.Foreground?.Green).Append(',').Append(style.Foreground?.Blue)
                    .Append('\u001d')
                    .Append(style.Background?.Red).Append(',').Append(style.Background?.Green).Append(',').Append(style.Background?.Blue)
                    .Append('\u001d')
                    .Append((byte)style.TextAttributes)
                    .Append('\u001e');
            }

            return builder.ToString();
        }

        public static ProjectionColorValue? CloneColor(ProjectionColorValue? color) {
            return color is null
                ? null
                : new ProjectionColorValue {
                    Red = color.Red,
                    Green = color.Green,
                    Blue = color.Blue,
                };
        }
    }
}
