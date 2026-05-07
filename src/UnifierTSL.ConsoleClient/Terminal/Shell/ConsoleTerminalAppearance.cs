using System.Text;
using UnifierTSL.Contracts.Display;

namespace UnifierTSL.Terminal.Shell
{
    internal static class ConsoleTerminalAppearance
    {
        private static readonly ConsolePaletteEntry[] ConsolePalette = [
            new(ConsoleColor.Black, 0, 0, 0),
            new(ConsoleColor.DarkBlue, 0, 0, 128),
            new(ConsoleColor.DarkGreen, 0, 128, 0),
            new(ConsoleColor.DarkCyan, 0, 128, 128),
            new(ConsoleColor.DarkRed, 128, 0, 0),
            new(ConsoleColor.DarkMagenta, 128, 0, 128),
            new(ConsoleColor.DarkYellow, 128, 128, 0),
            new(ConsoleColor.Gray, 192, 192, 192),
            new(ConsoleColor.DarkGray, 128, 128, 128),
            new(ConsoleColor.Blue, 0, 0, 255),
            new(ConsoleColor.Green, 0, 255, 0),
            new(ConsoleColor.Cyan, 0, 255, 255),
            new(ConsoleColor.Red, 255, 0, 0),
            new(ConsoleColor.Magenta, 255, 0, 255),
            new(ConsoleColor.Yellow, 255, 255, 0),
            new(ConsoleColor.White, 255, 255, 255),
        ];
        private static readonly StyledColorValue StatusWarningBackground = new() {
            Red = 255,
            Green = 135,
            Blue = 0,
        };

        public static string FormatAnsi(
            StyledColorValue? foreground = null,
            StyledColorValue? background = null,
            StyledTextAttributes textAttributes = StyledTextAttributes.None) {
            var builder = new StringBuilder().Append(AnsiColorCodec.Escape);
            bool hasContent = false;
            if ((textAttributes & StyledTextAttributes.Underline) != 0) {
                builder.Append('4');
                hasContent = true;
            }

            if (foreground is { } foregroundValue) {
                if (hasContent) {
                    builder.Append(';');
                }

                AppendAnsiColor(builder, foregroundValue, background: false);
                hasContent = true;
            }

            if (background is { } backgroundValue) {
                if (hasContent) {
                    builder.Append(';');
                }

                AppendAnsiColor(builder, backgroundValue, background: true);
                hasContent = true;
            }

            if (!hasContent) {
                return AnsiColorCodec.Reset;
            }

            return builder.Append('m').ToString();
        }

        public static ConsoleColor ResolveConsoleColor(StyledColorValue color) {
            var bestDistance = long.MaxValue;
            var bestMatch = ConsoleColor.Gray;
            foreach (var entry in ConsolePalette) {
                var red = color.Red - entry.Value.Red;
                var green = color.Green - entry.Value.Green;
                var blue = color.Blue - entry.Value.Blue;
                var distance = (long)red * red + (long)green * green + (long)blue * blue;
                if (distance >= bestDistance) {
                    continue;
                }

                bestDistance = distance;
                bestMatch = entry.Color;
            }

            return bestMatch;
        }

        private static void AppendAnsiColor(StringBuilder builder, StyledColorValue color, bool background) {
            if (TryGetAnsiColorCode(color, background, out var code)) {
                builder.Append(code);
                return;
            }

            builder
                .Append(background ? "48;2;" : "38;2;")
                .Append(color.Red)
                .Append(';')
                .Append(color.Green)
                .Append(';')
                .Append(color.Blue);
        }

        private static bool TryGetAnsiColorCode(StyledColorValue color, bool background, out string code) {
            if (TryResolvePaletteColor(color, out var consoleColor)) {
                code = (background
                        ? AnsiColorCodec.GetBackgroundCode(consoleColor)
                        : AnsiColorCodec.GetForegroundCode(consoleColor))
                    .ToString();
                return true;
            }

            if (Matches(color, StatusWarningBackground)) {
                code = background ? "48;5;208" : "38;5;208";
                return true;
            }

            code = string.Empty;
            return false;
        }

        public static StyledColorValue ResolveStyledColor(ConsoleColor color) {
            return ConsolePalette.First(entry => entry.Color == color).Value;
        }

        private static bool TryResolvePaletteColor(StyledColorValue color, out ConsoleColor consoleColor) {
            foreach (var entry in ConsolePalette) {
                if (!Matches(color, entry.Value)) {
                    continue;
                }

                consoleColor = entry.Color;
                return true;
            }

            consoleColor = default;
            return false;
        }

        private static bool Matches(StyledColorValue left, StyledColorValue right) {
            return left.Red == right.Red
                && left.Green == right.Green
                && left.Blue == right.Blue;
        }

        private readonly record struct ConsolePaletteEntry(ConsoleColor Color, byte Red, byte Green, byte Blue)
        {
            public StyledColorValue Value { get; } = new StyledColorValue {
                Red = Red,
                Green = Green,
                Blue = Blue,
            };
        }
    }
}
