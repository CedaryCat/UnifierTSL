using System.Collections.Immutable;

namespace UnifierTSL.Surface.Prompting.Semantics;

/*
    PromptInputLexer is the prompt-side lexical contract for command input.

    This file is intentionally kept behaviorally aligned with the command-side tokenizers
    that operators already rely on.
    Quote handling, backslash escaping, and "empty quoted token means a real slot exists"
    are not cosmetic details: analyzer focus, candidate filtering, ghost preview, and the
    final command binder all depend on them staying in sync.

    Be cautious when editing this file. Do not simplify quoting/escaping rules just because
    a narrower prompt scenario seems to work. Removing one of these edge cases usually causes
    a later regression such as:
    - ghost text no longer previewing `"fallen star"`-style candidates,
    - a trailing space no longer opening the next semantic continuation slot,
    - prompt acceptance diverging from the runtime parser.

    This lexer intentionally stops at lexical facts. It reports token spans plus the raw tail
    state after the last separator, but it does not decide whether the token before a trailing
    separator is already committed or still the live semantic edit target. That decision depends
    on the active route/slot and belongs in PromptSemanticAnalyzer.

    Preserve existing nearby comments unless the underlying mechanism truly changes. If a
    mechanism changes, update the comment in the same change so future edits do not "clean up"
    a rule that is actually required for compatibility.
*/
public readonly record struct PromptInputToken(
    string Value,
    int StartIndex,
    int SourceLength,
    bool Quoted,
    bool LeadingCharacterEscaped)
{
    public int EndIndex => StartIndex + SourceLength;
}

internal readonly record struct PromptInputParseResult(
    string InputText,
    string Prefix,
    string BodyText,
    int BodyOffset,
    ImmutableArray<PromptInputToken> Tokens,
    string TailText,
    int TailTextStart,
    bool TailTextQuoted,
    bool TailTextLeadingCharacterEscaped,
    bool HasTrailingSeparator);

internal static class PromptInputLexer
{
    public static PromptInputParseResult Parse(string? input, IReadOnlyList<string>? activationPrefixes = null) {
        var text = input ?? string.Empty;

        var leftPaddingLength = 0;
        while (leftPaddingLength < text.Length && char.IsWhiteSpace(text[leftPaddingLength])) {
            leftPaddingLength += 1;
        }

        var remaining = leftPaddingLength < text.Length
            ? text[leftPaddingLength..]
            : string.Empty;
        var prefix = ResolvePrefix(remaining, activationPrefixes);
        var bodyText = prefix.Length > 0
            ? remaining[prefix.Length..]
            : remaining;
        var bodyOffset = leftPaddingLength + prefix.Length;

        return ParseBody(text, prefix, bodyText, bodyOffset);
    }

    private static PromptInputParseResult ParseBody(string fullText, string prefix, string bodyText, int bodyOffset) {
        List<PromptInputToken> tokens = [];
        System.Text.StringBuilder current = new();
        var inQuotes = false;
        var tokenLeadingCharacterEscaped = false;
        var tokenStart = -1;
        var tokenEndExclusive = -1;
        var emptyQuotedTokenStart = -1;

        void CommitToken(bool quoted, bool allowEmpty = false) {
            if (current.Length == 0 && !(allowEmpty && tokenStart >= 0)) {
                tokenStart = -1;
                tokenEndExclusive = -1;
                tokenLeadingCharacterEscaped = false;
                return;
            }

            tokens.Add(new PromptInputToken(
                Value: current.ToString(),
                StartIndex: tokenStart >= 0 ? tokenStart : bodyOffset,
                SourceLength: tokenStart >= 0 && tokenEndExclusive >= tokenStart
                    ? tokenEndExclusive - tokenStart
                    : current.Length,
                Quoted: quoted,
                LeadingCharacterEscaped: tokenLeadingCharacterEscaped));
            current.Clear();
            tokenStart = -1;
            tokenEndExclusive = -1;
            tokenLeadingCharacterEscaped = false;
            emptyQuotedTokenStart = -1;
        }

        for (var index = 0; index < bodyText.Length; index++) {
            var currentChar = bodyText[index];

            if (currentChar == '\\' && index + 1 < bodyText.Length) {
                // Keep the same escape surface as the command parser: only quote, space, and
                // backslash are special, and other sequences preserve the leading slash.
                var escaped = bodyText[++index];
                var startsToken = tokenStart < 0 && current.Length == 0;
                if (tokenStart < 0) {
                    tokenStart = bodyOffset + index - 1;
                }

                if (startsToken) {
                    tokenLeadingCharacterEscaped = true;
                }

                if (escaped != '"' && escaped != ' ' && escaped != '\\') {
                    current.Append('\\');
                }

                current.Append(escaped);
                tokenEndExclusive = bodyOffset + index + 1;
                emptyQuotedTokenStart = -1;
                continue;
            }

            if (currentChar == '"') {
                // Empty quoted tokens are meaningful. They preserve the "user opened a slot"
                // state so prompt flow can advance the same way the command parser/binder will.
                if (inQuotes) {
                    CommitToken(quoted: true, allowEmpty: true);
                    inQuotes = false;
                }
                else {
                    if (current.Length > 0) {
                        CommitToken(quoted: false);
                    }

                    inQuotes = true;
                    tokenStart = bodyOffset + index + 1;
                    tokenEndExclusive = tokenStart;
                    tokenLeadingCharacterEscaped = false;
                    emptyQuotedTokenStart = tokenStart;
                }

                continue;
            }

            if (char.IsWhiteSpace(currentChar) && !inQuotes) {
                CommitToken(quoted: false);
                continue;
            }

            if (tokenStart < 0) {
                tokenStart = bodyOffset + index;
                tokenLeadingCharacterEscaped = false;
            }

            current.Append(currentChar);
            tokenEndExclusive = bodyOffset + index + 1;
            emptyQuotedTokenStart = -1;
        }

        if (current.Length > 0) {
            CommitToken(quoted: inQuotes);
        }

        var endsWithWhitespace = bodyText.Length > 0
            && char.IsWhiteSpace(bodyText[^1])
            && !inQuotes;
        var endsWithEmptyQuotedToken = inQuotes
            && current.Length == 0
            && emptyQuotedTokenStart >= 0;
        var hasTrailingSeparator = endsWithWhitespace || endsWithEmptyQuotedToken;

        if (tokens.Count == 0) {
            var tailTextStart = endsWithEmptyQuotedToken
                ? emptyQuotedTokenStart
                : bodyOffset;
            return new PromptInputParseResult(
                InputText: fullText,
                Prefix: prefix,
                BodyText: bodyText,
                BodyOffset: bodyOffset,
                Tokens: [],
                TailText: endsWithEmptyQuotedToken ? string.Empty : bodyText,
                TailTextStart: tailTextStart,
                TailTextQuoted: inQuotes,
                TailTextLeadingCharacterEscaped: false,
                HasTrailingSeparator: hasTrailingSeparator);
        }

        if (hasTrailingSeparator) {
            return new PromptInputParseResult(
                InputText: fullText,
                Prefix: prefix,
                BodyText: bodyText,
                BodyOffset: bodyOffset,
                Tokens: [.. tokens],
                TailText: string.Empty,
                TailTextStart: endsWithEmptyQuotedToken
                    ? emptyQuotedTokenStart
                    : bodyOffset + bodyText.Length,
                TailTextQuoted: endsWithEmptyQuotedToken,
                TailTextLeadingCharacterEscaped: false,
                HasTrailingSeparator: true);
        }

        var currentToken = tokens[^1];
        return new PromptInputParseResult(
            InputText: fullText,
            Prefix: prefix,
            BodyText: bodyText,
            BodyOffset: bodyOffset,
            Tokens: [.. tokens],
            TailText: currentToken.Value,
            TailTextStart: currentToken.StartIndex,
            TailTextQuoted: currentToken.Quoted,
            TailTextLeadingCharacterEscaped: currentToken.LeadingCharacterEscaped,
            HasTrailingSeparator: false);
    }

    private static string ResolvePrefix(string input, IReadOnlyList<string>? prefixes) {
        if (prefixes is null || prefixes.Count == 0) {
            return string.Empty;
        }

        foreach (var prefix in prefixes
            .Where(static prefix => !string.IsNullOrWhiteSpace(prefix))
            .Select(static prefix => prefix.Trim())
            .OrderByDescending(static prefix => prefix.Length)) {
            if (input.StartsWith(prefix, StringComparison.Ordinal)) {
                return prefix;
            }
        }

        return string.Empty;
    }
}
