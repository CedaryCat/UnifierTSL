namespace UnifierTSL.Terminal {
    public static class AnsiColorCodec {
        public const string Escape = "\u001b[";
        public const string Reset = "\u001b[0m";

        public static string GetSgr(ConsoleColor foreground, ConsoleColor background) {
            return $"{Escape}{GetForegroundCode(foreground)};{GetBackgroundCode(background)}m";
        }

        public static string Wrap(string text, ConsoleColor foreground, ConsoleColor background) {
            if (string.IsNullOrEmpty(text)) {
                return string.Empty;
            }

            return GetSgr(foreground, background) + text + Reset;
        }

        public static int GetForegroundCode(ConsoleColor color) {
            return color switch {
                ConsoleColor.Black => 30,
                ConsoleColor.DarkBlue => 34,
                ConsoleColor.DarkGreen => 32,
                ConsoleColor.DarkCyan => 36,
                ConsoleColor.DarkRed => 31,
                ConsoleColor.DarkMagenta => 35,
                ConsoleColor.DarkYellow => 33,
                ConsoleColor.Gray => 37,
                ConsoleColor.DarkGray => 90,
                ConsoleColor.Blue => 94,
                ConsoleColor.Green => 92,
                ConsoleColor.Cyan => 96,
                ConsoleColor.Red => 91,
                ConsoleColor.Magenta => 95,
                ConsoleColor.Yellow => 93,
                ConsoleColor.White => 97,
                _ => 37,
            };
        }

        public static int GetBackgroundCode(ConsoleColor color) {
            return color switch {
                ConsoleColor.Black => 40,
                ConsoleColor.DarkBlue => 44,
                ConsoleColor.DarkGreen => 42,
                ConsoleColor.DarkCyan => 46,
                ConsoleColor.DarkRed => 41,
                ConsoleColor.DarkMagenta => 45,
                ConsoleColor.DarkYellow => 43,
                ConsoleColor.Gray => 47,
                ConsoleColor.DarkGray => 100,
                ConsoleColor.Blue => 104,
                ConsoleColor.Green => 102,
                ConsoleColor.Cyan => 106,
                ConsoleColor.Red => 101,
                ConsoleColor.Magenta => 105,
                ConsoleColor.Yellow => 103,
                ConsoleColor.White => 107,
                _ => 40,
            };
        }
    }
}
