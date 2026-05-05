using System.Text;
using UnifierTSL.Contracts.Display;
using UnifierTSL.Terminal;

namespace UnifierTSL.Terminal.Shell
{
    internal static class TerminalStreamTextFormatter
    {
        public static string FormatStyledText(StyledTextLine line, StyleDictionary? styles) {
            var styleDictionary = StyleDictionaryOps.Merge(SurfaceStyleCatalog.Default, styles);
            var builder = new StringBuilder();
            var lineStyle = StyleDictionaryOps.Resolve(styleDictionary, line.LineStyleId);
            foreach (var run in line.Runs ?? []) {
                if (run is null || string.IsNullOrEmpty(run.Text)) {
                    continue;
                }

                AppendRun(builder, run.Text, ResolveRunStyle(styleDictionary, lineStyle, run.StyleId));
            }

            return builder.ToString();
        }

        private static void AppendRun(StringBuilder builder, string text, StyledTextStyle? style) {
            if (style is not { } activeStyle || IsEmptyStyle(activeStyle)) {
                builder.Append(AnsiSanitizer.SanitizeEscapes(text));
                return;
            }

            builder.Append(ConsoleTerminalAppearance.FormatAnsi(activeStyle.Foreground, activeStyle.Background, activeStyle.TextAttributes));
            builder.Append(AnsiSanitizer.SanitizeEscapes(text));
            builder.Append(AnsiColorCodec.Reset);
        }

        private static StyledTextStyle? ResolveRunStyle(
            StyleDictionary styleDictionary,
            StyledTextStyle? lineStyle,
            string? runStyleId) {
            var runStyle = StyleDictionaryOps.Resolve(styleDictionary, runStyleId);
            if (runStyle is null) {
                return lineStyle;
            }

            if (lineStyle is null) {
                return runStyle;
            }

            return new StyledTextStyle {
                StyleId = runStyle.StyleId,
                Slot = runStyle.Slot,
                Foreground = runStyle.Foreground ?? lineStyle.Foreground,
                Background = runStyle.Background ?? lineStyle.Background,
                TextAttributes = runStyle.TextAttributes,
            };
        }

        private static bool IsEmptyStyle(StyledTextStyle style) {
            return style.Foreground is null
                && style.Background is null
                && style.TextAttributes == StyledTextAttributes.None;
        }
    }
}
