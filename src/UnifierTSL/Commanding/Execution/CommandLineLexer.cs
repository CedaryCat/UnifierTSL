using System.Collections.Immutable;
using UnifierTSL.Surface.Prompting;
using UnifierTSL.Surface.Prompting.Semantics;

namespace UnifierTSL.Commanding.Execution
{
    public readonly record struct CommandBodyParseResult(
        ImmutableArray<CommandInputToken> Tokens,
        ImmutableArray<CommandInputToken> ArgumentTokens,
        string CurrentToken,
        int CurrentTokenStart,
        bool CurrentTokenQuoted,
        bool CurrentTokenLeadingCharacterEscaped,
        bool IsCommandToken,
        int ArgumentIndex,
        bool EndsWithSpace);

    public readonly record struct CommandTextParseResult(
        string InputText,
        string Prefix,
        string CommandBody,
        int BodyOffset,
        CommandBodyParseResult Body)
    {
        public ImmutableArray<CommandInputToken> Tokens => Body.Tokens;

        public ImmutableArray<CommandInputToken> ArgumentTokens => Body.ArgumentTokens;

        public string CommandName => Tokens.Length > 0 ? Tokens[0].Value : string.Empty;
    }

    public static class CommandLineLexer
    {
        public static bool TryParseCommandLine(
            string? input,
            IReadOnlyList<string>? commandPrefixes,
            out string commandName,
            out ImmutableArray<CommandInputToken> argumentTokens) {
            var parse = ParseCommandText(input, commandPrefixes);
            if (parse.Tokens.Length == 0) {
                commandName = string.Empty;
                argumentTokens = [];
                return false;
            }

            var commandToken = parse.Tokens[0];
            if (string.IsNullOrWhiteSpace(commandToken.Value)) {
                commandName = string.Empty;
                argumentTokens = [];
                return false;
            }

            commandName = commandToken.Value;
            argumentTokens = parse.ArgumentTokens;
            return true;
        }

        public static CommandTextParseResult ParseCommandText(string? input, IReadOnlyList<string>? commandPrefixes) {
            var parsed = PromptInputLexer.Parse(input, commandPrefixes);
            ImmutableArray<CommandInputToken> tokens = [.. parsed.Tokens.Select(static token => new CommandInputToken(
                token.Value,
                token.StartIndex,
                token.SourceLength,
                token.Quoted,
                token.LeadingCharacterEscaped))];

            var isCommandToken = tokens.Length == 1 && !parsed.HasTrailingSeparator;
            ImmutableArray<CommandInputToken> argumentTokens = tokens.Length > 1
                ? [.. tokens.Skip(1)]
                : [];
            var argumentIndex = tokens.Length == 0
                ? -1
                : parsed.HasTrailingSeparator
                    ? Math.Max(0, argumentTokens.Length)
                    : Math.Max(0, argumentTokens.Length - 1);

            return new CommandTextParseResult(
                InputText: parsed.InputText,
                Prefix: parsed.Prefix,
                CommandBody: parsed.BodyText,
                BodyOffset: parsed.BodyOffset,
                Body: new CommandBodyParseResult(
                    Tokens: tokens,
                    ArgumentTokens: argumentTokens,
                    CurrentToken: parsed.TailText,
                    CurrentTokenStart: parsed.TailTextStart,
                    CurrentTokenQuoted: parsed.TailTextQuoted,
                    CurrentTokenLeadingCharacterEscaped: parsed.TailTextLeadingCharacterEscaped,
                    IsCommandToken: isCommandToken,
                    ArgumentIndex: tokens.Length == 0 ? -1 : isCommandToken ? -1 : argumentIndex,
                    EndsWithSpace: parsed.HasTrailingSeparator));
        }

        public static CommandBodyParseResult ParseCommandBody(string commandBody, int bodyOffset) {
            var parsed = PromptInputLexer.Parse(commandBody);
            ImmutableArray<CommandInputToken> tokens = [.. parsed.Tokens.Select(token => new CommandInputToken(
                token.Value,
                bodyOffset + token.StartIndex,
                token.SourceLength,
                token.Quoted,
                token.LeadingCharacterEscaped))];
            var isCommandToken = tokens.Length == 1 && !parsed.HasTrailingSeparator;
            ImmutableArray<CommandInputToken> argumentTokens = tokens.Length > 1
                ? [.. tokens.Skip(1)]
                : [];
            var argumentIndex = parsed.HasTrailingSeparator
                ? Math.Max(0, argumentTokens.Length)
                : Math.Max(0, argumentTokens.Length - 1);

            return new CommandBodyParseResult(
                Tokens: tokens,
                ArgumentTokens: argumentTokens,
                CurrentToken: parsed.TailText,
                CurrentTokenStart: bodyOffset + parsed.TailTextStart,
                CurrentTokenQuoted: parsed.TailTextQuoted,
                CurrentTokenLeadingCharacterEscaped: parsed.TailTextLeadingCharacterEscaped,
                IsCommandToken: isCommandToken,
                ArgumentIndex: tokens.Length == 0 ? -1 : isCommandToken ? -1 : argumentIndex,
                EndsWithSpace: parsed.HasTrailingSeparator);
        }
    }
}
