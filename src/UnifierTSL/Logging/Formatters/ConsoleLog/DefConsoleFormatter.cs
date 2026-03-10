using Microsoft.Xna.Framework;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using UnifierTSL.Extensions;

namespace UnifierTSL.Logging.Formatters.ConsoleLog
{
    public class DefConsoleFormatter : ILogFormatter<ColoredSegment>
    {
        public string FormatName => "Default Console Formatter";
        public string Description => "A default console formatter implementation";

        private static readonly char[] separator = ['\r', '\n'];

        public void Format(scoped in LogEntry entry, scoped ref ColoredSegment buffer, out int written) {
            written = 0;

            string levelText = entry.Level switch {
                LogLevel.Trace => "[Trace]",
                LogLevel.Debug => "[Debug]",
                LogLevel.Info => "[+Info]",
                LogLevel.Success => "[Succe]",
                LogLevel.Warning => "[+Warn]",
                LogLevel.Error => "[Error]",
                LogLevel.Critical => "[Criti]",
                _ => "[+-·-+]"
            };
            string roleCatText = string.IsNullOrEmpty(entry.Category)
                ? $"[{entry.Role}]"
                : $"[{entry.Role}|{entry.Category}]";

            ConsoleColor fg = GetLevelColor(entry.Level);
            ConsoleColor bg = ConsoleColor.Black;

            // Segment 0: [Level]
            Unsafe.Add(ref buffer, written) = new(levelText.AsMemory(), fg, bg);
            written++;

            // Segment 1: [Role|Category]
            Unsafe.Add(ref buffer, written) = new(roleCatText.AsMemory(), GetRoleCategoryFg(in entry), GetRoleCategoryBg(in entry));
            written++;

            // Segment 2: Main Message
            string[] lines = entry.Message.Split(separator, StringSplitOptions.RemoveEmptyEntries);
            string messageText = lines.Length switch {
                0 => " \r\n",
                1 => $" {lines[0]}\r\n",
                _ => $" {lines[0]}\r\n{FormatMultiline(lines.Skip(1))}\r\n"
            };

            WriteMessageSegmentsWithColorTags(messageText, fg, bg, ref buffer, ref written);

            // Segment 3: Exception (optional)
            if (entry.Exception != null) {
                bool isHandled = entry.Level <= LogLevel.Warning;
                string label = isHandled ? "Handled Exception:" : "Unexpected Exception:";
                string[] exceptionLines = entry.Exception.ToString().Split(separator, StringSplitOptions.RemoveEmptyEntries);

                StringBuilder sb = new();
                sb.AppendLine($" │ {label}");

                for (int i = 0; i < exceptionLines.Length; i++) {
                    if (i == exceptionLines.Length - 1)
                        sb.AppendLine($" └── {exceptionLines[i]}");
                    //else if (i == 0)
                    //    sb.AppendLine($" ├── {exceptionLines[i]}");
                    else
                        sb.AppendLine($" │   {exceptionLines[i]}");
                }

                Unsafe.Add(ref buffer, written) = new(sb.ToString().AsMemory(), ConsoleColor.Red, ConsoleColor.White);
                written++;
            }
        }

        private static ConsoleColor GetLevelColor(LogLevel level) => level switch {
            LogLevel.Trace => ConsoleColor.Gray,
            LogLevel.Debug => ConsoleColor.Blue,
            LogLevel.Info => ConsoleColor.White,
            LogLevel.Success => ConsoleColor.Green,
            LogLevel.Warning => ConsoleColor.Yellow,
            LogLevel.Error => ConsoleColor.Red,
            LogLevel.Critical => ConsoleColor.DarkRed,
            _ => ConsoleColor.White
        };

        private static ConsoleColor GetRoleCategoryFg(scoped in LogEntry entry) => ConsoleColor.Cyan;
        private static ConsoleColor GetRoleCategoryBg(scoped in LogEntry entry) => ConsoleColor.Black;

        private static void WriteMessageSegmentsWithColorTags(string messageText, ConsoleColor defaultFg, ConsoleColor bg, scoped ref ColoredSegment buffer, scoped ref int written) {
            int plainStart = 0;
            int searchStart = 0;

            while (searchStart < messageText.Length) {
                int tagStart = messageText.IndexOf('[', searchStart);
                if (tagStart < 0) {
                    break;
                }

                if (!TryParseColorTag(messageText, tagStart, out int innerTextStart, out int innerTextLength, out ConsoleColor tagFg, out int tagEndExclusive)) {
                    searchStart = tagStart + 1;
                    continue;
                }

                if (tagStart > plainStart) {
                    AppendSegment(messageText, plainStart, tagStart - plainStart, defaultFg, bg, ref buffer, ref written);
                }

                AppendSegment(messageText, innerTextStart, innerTextLength, tagFg, bg, ref buffer, ref written);

                plainStart = tagEndExclusive;
                searchStart = tagEndExclusive;
            }

            if (plainStart < messageText.Length) {
                AppendSegment(messageText, plainStart, messageText.Length - plainStart, defaultFg, bg, ref buffer, ref written);
            }
        }

        private static void AppendSegment(string source, int start, int length, ConsoleColor fg, ConsoleColor bg, scoped ref ColoredSegment buffer, scoped ref int written) {
            if (length <= 0) {
                return;
            }

            Unsafe.Add(ref buffer, written) = new(source.AsMemory(start, length), fg, bg);
            written++;
        }

        private static bool TryParseColorTag(
            string text,
            int tagStart,
            out int innerTextStart,
            out int innerTextLength,
            out ConsoleColor fg,
            out int tagEndExclusive) {
            innerTextStart = 0;
            innerTextLength = 0;
            fg = ConsoleColor.White;
            tagEndExclusive = 0;

            if (tagStart < 0 || tagStart >= text.Length || text[tagStart] != '[') {
                return false;
            }

            int colorHexStart;
            int contentStart = tagStart + 1;
            ReadOnlySpan<char> remain = text.AsSpan(contentStart);

            if (remain.StartsWith("c/", StringComparison.OrdinalIgnoreCase)) {
                colorHexStart = tagStart + 3;
            }
            else if (remain.StartsWith("color/", StringComparison.OrdinalIgnoreCase)) {
                colorHexStart = tagStart + 7;
            }
            else {
                return false;
            }

            int firstRightBracket = text.IndexOf(']', colorHexStart);
            if (firstRightBracket < 0) {
                return false;
            }

            int separatorIndex = text.IndexOf(':', colorHexStart);
            if (separatorIndex < 0 || separatorIndex > firstRightBracket) {
                return false;
            }

            ReadOnlySpan<char> colorHex = text.AsSpan(colorHexStart, separatorIndex - colorHexStart).Trim();
            if (!TryParseRgbHexColor(colorHex, out Color color)) {
                return false;
            }

            fg = color.ToConsoleColor();
            innerTextStart = separatorIndex + 1;
            innerTextLength = firstRightBracket - innerTextStart;
            tagEndExclusive = firstRightBracket + 1;
            return true;
        }

        private static bool TryParseRgbHexColor(ReadOnlySpan<char> hex, out Color color) {
            color = Color.White;

            if (hex.Length == 7 && hex[0] == '#') {
                hex = hex[1..];
            }

            if (hex.Length == 3) {
                if (!TryParseHexNibble(hex[0], out byte r) ||
                    !TryParseHexNibble(hex[1], out byte g) ||
                    !TryParseHexNibble(hex[2], out byte b)) {
                    return false;
                }

                color = new Color(
                    (byte)((r << 4) | r),
                    (byte)((g << 4) | g),
                    (byte)((b << 4) | b));
                return true;
            }

            if (hex.Length != 6) {
                return false;
            }

            if (!byte.TryParse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte rr) ||
                !byte.TryParse(hex.Slice(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte gg) ||
                !byte.TryParse(hex.Slice(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte bb)) {
                return false;
            }

            color = new Color(rr, gg, bb);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryParseHexNibble(char c, out byte value) {
            if (c is >= '0' and <= '9') {
                value = (byte)(c - '0');
                return true;
            }

            if (c is >= 'a' and <= 'f') {
                value = (byte)(c - 'a' + 10);
                return true;
            }

            if (c is >= 'A' and <= 'F') {
                value = (byte)(c - 'A' + 10);
                return true;
            }

            value = 0;
            return false;
        }

        private static string FormatMultiline(IEnumerable<string> lines) {
            StringBuilder sb = new();
            string[] arr = [.. lines];
            for (int i = 0; i < arr.Length; i++) {
                if (i == arr.Length - 1)
                    sb.AppendLine($" └── {arr[i]}");
                //else if (i == 0)
                //    sb.AppendLine($" ├── {arr[i]}");
                else
                    sb.AppendLine($" │   {arr[i]}");
            }
            return sb.ToString().TrimEnd();
        }

        public int GetEstimatedSize(scoped in LogEntry entry) {
            return (entry.Exception is null ? 3 : 4)
                + CountSubstring(entry.Message, "[c/") * 2
                + CountSubstring(entry.Message, "[color/") * 2;
        }

        static int CountSubstring(string source, string sub) {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(sub))
                return 0;

            int count = 0;
            int index = 0;

            while ((index = source.IndexOf(sub, index, StringComparison.OrdinalIgnoreCase)) != -1) {
                count++;
                index += sub.Length;
            }

            return count;
        }
    }
}
