using UnifierTSL.Contracts.Display;
using UnifierTSL.Contracts.Projection;

namespace UnifierTSL.Terminal.Runtime {
    internal static class ProjectionStyleDictionaryAdapter {
        public static StyleDictionary ToDisplayStyleDictionary(
            ProjectionStyleDictionary? styles,
            params StyleDictionary?[] fallbacks) {
            Dictionary<string, StyledTextStyle> stylesById = new(StringComparer.Ordinal);
            foreach (var fallback in fallbacks ?? []) {
                foreach (var style in fallback?.Styles ?? []) {
                    if (style is null || string.IsNullOrWhiteSpace(style.StyleId)) {
                        continue;
                    }

                    stylesById[style.StyleId] = style;
                }
            }

            foreach (var style in styles?.Styles ?? []) {
                if (style is null || string.IsNullOrWhiteSpace(style.Key)) {
                    continue;
                }

                stylesById[style.Key] = new StyledTextStyle {
                    StyleId = style.Key,
                    Slot = style.Slot,
                    Foreground = ToDisplayColor(style.Foreground),
                    Background = ToDisplayColor(style.Background),
                    TextAttributes = ToDisplayTextAttributes(style.TextAttributes),
                };
            }

            return new StyleDictionary {
                Styles = [.. stylesById.Values],
            };
        }

        private static StyledColorValue? ToDisplayColor(ProjectionColorValue? color) {
            return color is null
                ? null
                : new StyledColorValue {
                    Red = color.Red,
                    Green = color.Green,
                    Blue = color.Blue,
                };
        }

        private static StyledTextAttributes ToDisplayTextAttributes(ProjectionTextAttributes attributes) {
            return (StyledTextAttributes)(byte)attributes;
        }
    }
}
