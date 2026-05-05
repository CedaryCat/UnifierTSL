using UnifierTSL.Commanding.Execution;

namespace Atelier.Commanding.Meta
{
    internal readonly record struct MetaCommandArgumentEdit(
        MetaCommandInfo Command,
        int ArgumentIndex,
        int StartIndex,
        int Length,
        string Prefix);

    internal static class MetaCommandParser
    {
        private static readonly IReadOnlyList<string> CommandPrefixes = [":"];

        public static bool TryParseSubmittedBuffer(string? submittedBuffer, out MetaCommand command) {
            var text = submittedBuffer ?? string.Empty;
            if (!TryFindFirstNonWhitespaceIndex(text, out var firstNonWhitespaceIndex) || text[firstNonWhitespaceIndex] != ':') {
                command = default!;
                return false;
            }

            var headerStart = ResolveLineStart(text, firstNonWhitespaceIndex);
            var headerEnd = ResolveLineEnd(text, firstNonWhitespaceIndex);
            var hasBodyLines = headerEnd < text.Length;
            var headerLine = text[headerStart..headerEnd];
            var bodyText = hasBodyLines
                ? text[SkipLineBreak(text, headerEnd)..]
                : string.Empty;
            var parse = CommandLineLexer.ParseCommandText(headerLine, CommandPrefixes);
            var commandName = parse.CommandName.Trim();
            var commandTokenEnd = parse.Tokens.Length > 0
                ? Math.Clamp(parse.Tokens[0].EndIndex, 0, headerLine.Length)
                : headerLine.Length;
            var headerRemainder = commandTokenEnd < headerLine.Length
                ? headerLine[commandTokenEnd..]
                : string.Empty;
            var transientCode = hasBodyLines ? bodyText : headerRemainder;

            command = new MetaCommand(
                ResolveKind(commandName),
                commandName,
                headerLine,
                headerRemainder,
                bodyText,
                transientCode);
            return true;
        }

        public static bool TryResolveCommandNameToken(
            string? sourceText,
            int caretIndex,
            out int startIndex,
            out int length,
            out string prefix) {
            var text = sourceText ?? string.Empty;
            if (!TryFindFirstNonWhitespaceIndex(text, out var firstNonWhitespaceIndex) || text[firstNonWhitespaceIndex] != ':') {
                startIndex = -1;
                length = 0;
                prefix = string.Empty;
                return false;
            }

            var headerEnd = ResolveLineEnd(text, firstNonWhitespaceIndex);
            var commandStart = firstNonWhitespaceIndex + 1;
            var caret = Math.Clamp(caretIndex, 0, text.Length);
            if (caret < commandStart || caret > headerEnd) {
                startIndex = -1;
                length = 0;
                prefix = string.Empty;
                return false;
            }

            var commandEnd = commandStart;
            while (commandEnd < headerEnd && !char.IsWhiteSpace(text[commandEnd])) {
                commandEnd++;
            }

            if (caret > commandEnd) {
                startIndex = -1;
                length = 0;
                prefix = string.Empty;
                return false;
            }

            startIndex = firstNonWhitespaceIndex;
            length = commandEnd - firstNonWhitespaceIndex;
            prefix = text[commandStart..caret];
            return true;
        }

        public static bool TryResolveArgumentToken(string? sourceText, int caretIndex, out MetaCommandArgumentEdit edit) {
            var text = sourceText ?? string.Empty;
            if (!TryFindFirstNonWhitespaceIndex(text, out var firstNonWhitespaceIndex) || text[firstNonWhitespaceIndex] != ':') {
                edit = default;
                return false;
            }

            var headerStart = ResolveLineStart(text, firstNonWhitespaceIndex);
            var headerEnd = ResolveLineEnd(text, firstNonWhitespaceIndex);
            var caret = Math.Clamp(caretIndex, 0, text.Length);
            if (caret <= firstNonWhitespaceIndex || caret > headerEnd) {
                edit = default;
                return false;
            }

            var headerPrefix = text[headerStart..caret];
            var parse = CommandLineLexer.ParseCommandText(headerPrefix, CommandPrefixes);
            if (parse.Body.IsCommandToken
                || parse.Body.ArgumentIndex < 0
                || parse.Body.CurrentTokenQuoted
                || parse.Body.CurrentTokenLeadingCharacterEscaped
                || !MetaCommands.TryResolve(parse.CommandName, out var command)) {
                edit = default;
                return false;
            }

            var startIndex = headerStart + Math.Clamp(parse.Body.CurrentTokenStart, 0, headerPrefix.Length);
            var tokenEnd = startIndex;
            while (tokenEnd < headerEnd && !char.IsWhiteSpace(text[tokenEnd])) {
                tokenEnd++;
            }

            edit = new MetaCommandArgumentEdit(
                command,
                parse.Body.ArgumentIndex,
                startIndex,
                Math.Max(0, tokenEnd - startIndex),
                parse.Body.CurrentToken);
            return true;
        }

        private static MetaCommandKind ResolveKind(string commandName) {
            return MetaCommands.ResolveKind(commandName);
        }

        private static bool TryFindFirstNonWhitespaceIndex(string text, out int index) {
            for (var i = 0; i < text.Length; i++) {
                if (!char.IsWhiteSpace(text[i])) {
                    index = i;
                    return true;
                }
            }

            index = -1;
            return false;
        }

        private static int ResolveLineStart(string text, int index) {
            var current = Math.Clamp(index, 0, text.Length);
            while (current > 0 && text[current - 1] is not '\r' and not '\n') {
                current--;
            }

            return current;
        }

        private static int ResolveLineEnd(string text, int index) {
            var current = Math.Clamp(index, 0, text.Length);
            while (current < text.Length && text[current] is not '\r' and not '\n') {
                current++;
            }

            return current;
        }

        private static int SkipLineBreak(string text, int index) {
            if (index >= text.Length) {
                return text.Length;
            }

            if (text[index] == '\r' && index + 1 < text.Length && text[index + 1] == '\n') {
                return index + 2;
            }

            return index + 1;
        }
    }
}
