using System.Buffers;
using System.Globalization;
using System.Text;

namespace UnifierTSL.Terminal.Shell {
    internal static class TerminalCellWidth {
        public static int Measure(string? text) {
            return string.IsNullOrEmpty(text) ? 0 : Measure(text.AsSpan());
        }

        public static int Measure(ReadOnlySpan<char> text) {
            int width = 0;
            int index = 0;

            while (index < text.Length) {
                DecodeRune(text.Slice(index), out int codePoint, out UnicodeCategory category, out int consumed);
                width += GetCellWidth(codePoint, category);
                index += consumed;
            }

            return width;
        }

        public static int MeasurePrefix(string text, int utf16Length) {
            int bounded = Math.Clamp(utf16Length, 0, text.Length);
            return Measure(text.AsSpan(0, bounded));
        }

        public static int FindIndexByCols(string text, int columns, out int actualColumns) {

            int targetColumns = Math.Max(0, columns);
            int index = 0;
            int consumedColumns = 0;

            while (index < text.Length) {
                DecodeRune(text.AsSpan(index), out int codePoint, out UnicodeCategory category, out int consumed);
                int runeWidth = GetCellWidth(codePoint, category);
                if (consumedColumns + runeWidth > targetColumns) {
                    break;
                }

                consumedColumns += runeWidth;
                index += consumed;
            }

            actualColumns = consumedColumns;
            return index;
        }

        public static int TakeLengthByCols(string text, int startIndex, int maxColumns, out int consumedColumns) {

            int boundedStart = Math.Clamp(startIndex, 0, text.Length);
            int boundedColumns = Math.Max(0, maxColumns);
            int index = boundedStart;
            int width = 0;

            while (index < text.Length) {
                DecodeRune(text.AsSpan(index), out int codePoint, out UnicodeCategory category, out int consumed);
                int runeWidth = GetCellWidth(codePoint, category);

                if (width + runeWidth > boundedColumns) {
                    if (width == 0 && boundedColumns > 0) {
                        width += runeWidth;
                        index += consumed;
                    }
                    break;
                }

                width += runeWidth;
                index += consumed;
            }

            consumedColumns = width;
            return index - boundedStart;
        }

        private static void DecodeRune(ReadOnlySpan<char> text, out int codePoint, out UnicodeCategory category, out int consumed) {
            OperationStatus status = Rune.DecodeFromUtf16(text, out Rune rune, out int decodeConsumed);
            if (status == OperationStatus.Done && decodeConsumed > 0) {
                codePoint = rune.Value;
                category = Rune.GetUnicodeCategory(rune);
                consumed = decodeConsumed;
                return;
            }

            codePoint = text[0];
            category = char.GetUnicodeCategory(text[0]);
            consumed = 1;
        }

        private static int GetCellWidth(int codePoint, UnicodeCategory category) {
            if (codePoint == '\t') {
                return 4;
            }

            if (category is UnicodeCategory.NonSpacingMark
                or UnicodeCategory.EnclosingMark
                or UnicodeCategory.Format
                or UnicodeCategory.Control) {
                return 0;
            }

            if (codePoint < 0x20 || (codePoint >= 0x7F && codePoint < 0xA0)) {
                return 0;
            }

            return IsWideCodePoint(codePoint) ? 2 : 1;
        }

        private static bool IsWideCodePoint(int codePoint) {
            return codePoint switch {
                >= 0x1100 and <= 0x115F => true,
                0x2329 or 0x232A => true,
                >= 0x2E80 and <= 0x303E => true,
                >= 0x3040 and <= 0xA4CF => true,
                >= 0xAC00 and <= 0xD7A3 => true,
                >= 0xF900 and <= 0xFAFF => true,
                >= 0xFE10 and <= 0xFE19 => true,
                >= 0xFE30 and <= 0xFE6B => true,
                >= 0xFF01 and <= 0xFF60 => true,
                >= 0xFFE0 and <= 0xFFE6 => true,
                >= 0x1F300 and <= 0x1FAFF => true,
                >= 0x20000 and <= 0x2FFFD => true,
                >= 0x30000 and <= 0x3FFFD => true,
                _ => false,
            };
        }
    }
}
