using System.Text.RegularExpressions;

namespace UnifierTSL.ConsoleClient.Shell
{
    public static partial class AnsiSanitizer
    {
        [GeneratedRegex("\\x1B\\[[0-?]*[ -/]*[@-~]", RegexOptions.Compiled)]
        private static partial Regex CsiRegex();

        [GeneratedRegex("\\x1B\\][^\\x07]*(?:\\x07|\\x1B\\\\)", RegexOptions.Compiled)]
        private static partial Regex OscRegex();

        public static bool ContainsEscape(string? text)
        {
            return !string.IsNullOrEmpty(text) && text.IndexOf('\u001b') >= 0;
        }

        public static string SanitizeEscapes(string? text)
        {
            if (string.IsNullOrEmpty(text)) {
                return string.Empty;
            }

            return text.Replace("\u001b", "\\x1b", StringComparison.Ordinal);
        }

        public static string StripAnsi(string? text)
        {
            if (string.IsNullOrEmpty(text)) {
                return string.Empty;
            }

            string withoutOsc = OscRegex().Replace(text, string.Empty);
            return CsiRegex().Replace(withoutOsc, string.Empty);
        }
    }
}
