using System.Text;
using UnifierTSL.ConsoleClient.Shell;

namespace UnifierTSL.CLI.Sessions
{
    internal readonly record struct ReadLineCommandParse(
        bool IsCommandToken,
        string CommandName,
        ConsoleCommandHint? Hint,
        int ArgumentIndex,
        string CurrentToken,
        string TokenPrefix,
        int CurrentTokenStart,
        IReadOnlyList<string> ArgumentTokens,
        bool EndsWithSpace);

    internal sealed class ReadLineSemanticEngine
    {
        public IReadOnlyList<string> ResolveSuggestions(ReadLineResolvedContext context, string input)
        {
            if (context.UsePrecomputedCandidates) {
                return ResolvePrecomputedSuggestions(input, context);
            }

            if (context.Purpose == ConsoleInputPurpose.CommandLine) {
                return ResolveCommandLineSuggestions(input, context);
            }

            return ResolvePlainSuggestions(input, context);
        }

        public IReadOnlyList<string> ResolvePrecomputedSuggestions(string input, ReadLineResolvedContext context)
        {
            List<string> orderedValues = [];
            if (!string.IsNullOrWhiteSpace(context.GhostText)) {
                orderedValues.Add(context.GhostText);
            }

            orderedValues.AddRange(context.ResolveCandidates(ReadLineTargetKeys.Plain)
                .Where(static item => !string.IsNullOrWhiteSpace(item.Value))
                .Select(static item => item.Value));

            IReadOnlyList<string> ordered = DistinctPreserveOrder(orderedValues);
            if (string.IsNullOrEmpty(input)) {
                return ordered;
            }

            return ordered
                .Where(candidate => candidate.StartsWith(input, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        public IReadOnlyList<string> ResolvePlainSuggestions(string input, ReadLineResolvedContext context)
        {
            IReadOnlyList<ReadLineSuggestion> items = ResolvePlainSuggestionItems(context);
            return BuildCandidateList(input, items.Select(static item => item.Value), prefix: string.Empty, matchRawToken: false, preserveOrder: true);
        }

        public IReadOnlyList<ReadLineSuggestion> ResolvePlainSuggestionItems(ReadLineResolvedContext context)
        {
            List<ReadLineSuggestion> items = [];
            if (!string.IsNullOrWhiteSpace(context.GhostText)) {
                items.Add(new ReadLineSuggestion(context.GhostText, 1000));
            }

            items.AddRange(context.ResolveCandidates(ReadLineTargetKeys.Plain));

            return OrderSuggestionItems(items);
        }

        public IReadOnlyList<string> ResolveCommandLineSuggestions(string input, ReadLineResolvedContext context)
        {
            ReadLineCommandParse parse = ParseCommandInput(input, context);
            List<string> results = [];

            if (parse.IsCommandToken) {
                IEnumerable<string> commandNames = context.CommandHints.SelectMany(static hint => {
                    List<string> names = [hint.PrimaryName];
                    names.AddRange(hint.Aliases);
                    return names;
                });

                IEnumerable<string> uniqueNames = commandNames
                    .Where(static name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase);

                foreach (string name in uniqueNames) {
                    string full = parse.TokenPrefix + name;
                    if (string.IsNullOrEmpty(input) || full.StartsWith(input, StringComparison.OrdinalIgnoreCase)) {
                        results.Add(full);
                    }
                }

                return DistinctAndSort(results);
            }

            IReadOnlyList<string> literalSuggestions = ResolvePatternLiteralSuggestions(parse);
            if (literalSuggestions.Count > 0) {
                foreach (string candidate in BuildCandidateList(parse.CurrentToken, literalSuggestions, parse.TokenPrefix, matchRawToken: true)) {
                    results.Add(candidate);
                }
            }

            ReadLineTargetKey target = ResolveParameterTarget(parse, context);
            IEnumerable<string> rawCandidates = ResolveTargetCandidates(parse, context, target);

            foreach (string candidate in BuildCandidateList(parse.CurrentToken, rawCandidates, parse.TokenPrefix, matchRawToken: true)) {
                results.Add(candidate);
            }

            return DistinctAndSort(results);
        }

        public List<string> BuildStatusLines(
            ReadLineResolvedContext context,
            string? input)
        {
            ReadLineReactiveState state = new() {
                Purpose = context.Purpose,
                InputText = input ?? string.Empty,
                CursorIndex = 0,
                CompletionIndex = 0,
                CompletionCount = 0,
            };
            return BuildStatusLines(context, state);
        }

        public List<string> BuildStatusLines(
            ReadLineResolvedContext context,
            ReadLineReactiveState state,
            IReadOnlyList<string>? suggestions = null)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(state);

            List<string> lines = [.. context.StatusLines];
            if (context.SkipDerivedStatus) {
                return lines;
            }

            if (context.Purpose != ConsoleInputPurpose.CommandLine) {
                if (!string.IsNullOrWhiteSpace(context.HelpText)) {
                    lines.Add("help: " + context.HelpText);
                }

                if (!string.IsNullOrWhiteSpace(context.ParameterHint)) {
                    lines.Add("args: " + context.ParameterHint);
                }

                return lines;
            }

            string input = state.InputText ?? string.Empty;
            ReadLineCommandParse parse = ParseCommandInput(input, context);
            int completionIndex = ResolveGlobalCompletionIndex(state);
            ConsoleCommandHint? activeHint = ResolveActiveCommandHint(context, parse, completionIndex, suggestions, input);
            if (activeHint is null) {
                return lines;
            }

            string cmdHint = $"cmd : {activeHint.PrimaryName}";

            if (!string.IsNullOrWhiteSpace(activeHint.HelpText)) {
                cmdHint += " | " + activeHint.HelpText;
            }

            lines.Add(cmdHint);

            string argsLine = BuildCommandArgumentStatusLine(activeHint, parse, context.AllowAnsiStatusEscapes);

            if (parse.ArgumentIndex >= 0) {
                ReadLineTargetKey expected = ResolveParameterTarget(parse, context);
                lines.Add($"expect: {expected} {(string.IsNullOrWhiteSpace(argsLine)
                    ? $"(arg#{parse.ArgumentIndex + 1})"
                    : "| " + argsLine
                )}");
            }
            else {

                if (!string.IsNullOrWhiteSpace(argsLine)) {
                    lines.Add("args: " + argsLine);
                }
                else {
                    lines.Add($"expect: Command");
                }
            }

            return lines;
        }

        public ConsoleCommandHint? ResolveActiveCommandHint(
            ReadLineResolvedContext context,
            ReadLineReactiveState state,
            IReadOnlyList<string>? suggestions = null)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(state);

            string input = state.InputText ?? string.Empty;
            ReadLineCommandParse parse = ParseCommandInput(input, context);
            int completionIndex = ResolveGlobalCompletionIndex(state);
            return ResolveActiveCommandHint(context, parse, completionIndex, suggestions, input);
        }

        public ReadLineTargetKey ResolveParameterTarget(ReadLineCommandParse parse, ReadLineResolvedContext context)
        {
            if (parse.ArgumentIndex < 0) {
                return ReadLineTargetKeys.Plain;
            }

            if (TryResolvePatternParameter(parse, out ConsoleCommandParameterDescriptor? descriptor)) {
                return descriptor.Target;
            }

            if (parse.Hint is not null && parse.ArgumentIndex < parse.Hint.ParameterTargets.Length) {
                return parse.Hint.ParameterTargets[parse.ArgumentIndex];
            }

            return ReadLineTargetKeys.Plain;
        }

        public ReadLineCommandParse ParseCommandInput(string? input, ReadLineResolvedContext context)
        {
            string text = input ?? string.Empty;

            int leftPaddingLen = 0;
            while (leftPaddingLen < text.Length && char.IsWhiteSpace(text[leftPaddingLen])) {
                leftPaddingLen += 1;
            }

            string leftPadding = leftPaddingLen > 0 ? text[..leftPaddingLen] : string.Empty;
            string remaining = leftPaddingLen < text.Length ? text[leftPaddingLen..] : string.Empty;

            string prefix = context.CommandPrefixes
                .Where(static p => !string.IsNullOrWhiteSpace(p))
                .OrderByDescending(static p => p.Length)
                .FirstOrDefault(p => remaining.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                ?? string.Empty;

            string commandBody = prefix.Length > 0 ? remaining[prefix.Length..] : remaining;
            int bodyOffset = leftPadding.Length + prefix.Length;

            if (string.IsNullOrWhiteSpace(commandBody)) {
                return new ReadLineCommandParse(
                    IsCommandToken: true,
                    CommandName: string.Empty,
                    Hint: null,
                    ArgumentIndex: -1,
                    CurrentToken: commandBody,
                    TokenPrefix: leftPadding + prefix,
                    CurrentTokenStart: text.Length,
                    ArgumentTokens: [],
                    EndsWithSpace: false);
            }

            ParsedCommandBodyState parsedBody = ParseCommandBody(commandBody, bodyOffset);
            if (parsedBody.Tokens.Count == 0) {
                return new ReadLineCommandParse(
                    IsCommandToken: true,
                    CommandName: string.Empty,
                    Hint: null,
                    ArgumentIndex: -1,
                    CurrentToken: parsedBody.CurrentToken,
                    TokenPrefix: leftPadding + prefix,
                    CurrentTokenStart: Math.Clamp(parsedBody.CurrentTokenStart, 0, text.Length),
                    ArgumentTokens: [],
                    EndsWithSpace: parsedBody.EndsWithSpace);
            }

            ParsedCommandToken commandToken = parsedBody.Tokens[0];
            string commandName = commandToken.Value;
            ConsoleCommandHint? hint = FindCommandHint(commandName, context.CommandHints);

            if (parsedBody.IsCommandToken) {
                return new ReadLineCommandParse(
                    IsCommandToken: true,
                    CommandName: commandName,
                    Hint: hint,
                    ArgumentIndex: -1,
                    CurrentToken: commandName,
                    TokenPrefix: text[..Math.Clamp(commandToken.StartIndex, 0, text.Length)],
                    CurrentTokenStart: Math.Clamp(commandToken.StartIndex, 0, text.Length),
                    ArgumentTokens: [],
                    EndsWithSpace: false);
            }

            int currentTokenStart = Math.Clamp(parsedBody.CurrentTokenStart, 0, text.Length);
            string tokenPrefix = text[..currentTokenStart];

            return new ReadLineCommandParse(
                IsCommandToken: false,
                CommandName: commandName,
                Hint: hint,
                ArgumentIndex: parsedBody.ArgumentIndex,
                CurrentToken: parsedBody.CurrentToken,
                TokenPrefix: tokenPrefix,
                CurrentTokenStart: currentTokenStart,
                ArgumentTokens: parsedBody.ArgumentTokens.Select(static token => token.Value).ToList(),
                EndsWithSpace: parsedBody.EndsWithSpace);
        }

        private static ConsoleCommandHint? FindCommandHint(string commandName, IEnumerable<ConsoleCommandHint> hints)
        {
            foreach (ConsoleCommandHint hint in hints) {
                if (hint.PrimaryName.Equals(commandName, StringComparison.OrdinalIgnoreCase)) {
                    return hint;
                }
                if (hint.Aliases.Any(alias => alias.Equals(commandName, StringComparison.OrdinalIgnoreCase))) {
                    return hint;
                }
            }

            return null;
        }

        private static string BuildCommandArgumentStatusLine(
            ConsoleCommandHint hint,
            ReadLineCommandParse parse,
            bool allowAnsiStatusEscapes)
        {
            List<ArgumentDisplayToken> tokens = ResolveArgumentDisplayTokens(hint, parse);
            if (tokens.Count == 0) {
                return string.Empty;
            }

            int highlightIndex = ResolveHighlightArgumentIndex(tokens, parse.ArgumentIndex);
            if (highlightIndex >= 0) {
                ArgumentDisplayToken target = tokens[highlightIndex];
                tokens[highlightIndex] = target with { Text = HighlightToken(target.Text, allowAnsiStatusEscapes) };
            }

            return string.Join(' ', tokens.Select(static token => token.Text));
        }

        private static string HighlightToken(string token, bool allowAnsiStatusEscapes)
        {
            if (allowAnsiStatusEscapes) {
                return "\u001b[96;1m" + token + AnsiColorCodec.Reset;
            }

            return ">>" + token + "<<";
        }

        private static int ResolveHighlightArgumentIndex(IReadOnlyList<ArgumentDisplayToken> tokens, int argumentIndex)
        {
            if (argumentIndex < 0 || tokens.Count == 0) {
                return -1;
            }

            if (argumentIndex < tokens.Count) {
                return argumentIndex;
            }

            return tokens[^1].IsVariadic ? tokens.Count - 1 : -1;
        }

        private static List<ArgumentDisplayToken> ResolveArgumentDisplayTokens(ConsoleCommandHint hint, ReadLineCommandParse parse)
        {
            if (TryBuildPatternArgumentDisplayTokens(hint, parse, out List<ArgumentDisplayToken>? patternTokens)) {
                return patternTokens;
            }

            if (!string.IsNullOrWhiteSpace(hint.ParameterHint)) {
                return [.. hint.ParameterHint
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(static token => new ArgumentDisplayToken(
                        Text: token,
                        IsVariadic: token.Contains("...", StringComparison.Ordinal)))];
            }

            if (hint.ParameterTargets.Length > 0) {
                return [.. hint.ParameterTargets.Select((target, index) => new ArgumentDisplayToken(
                    Text: "<" + ResolveTargetDisplayName(target, index) + ">",
                    IsVariadic: false))];
            }

            return [];
        }

        private static bool TryBuildPatternArgumentDisplayTokens(
            ConsoleCommandHint hint,
            ReadLineCommandParse parse,
            out List<ArgumentDisplayToken> tokens)
        {
            tokens = [];
            if (!TrySelectPatternForDisplay(hint, parse, out ConsoleCommandPatternHint? pattern)) {
                return false;
            }

            foreach (string subCommand in pattern.SubCommands.Where(static sub => !string.IsNullOrWhiteSpace(sub))) {
                tokens.Add(new ArgumentDisplayToken(
                    Text: subCommand.Trim(),
                    IsVariadic: false));
            }

            foreach (ConsoleCommandParameterDescriptor parameter in pattern.Parameters) {
                string parameterName = string.IsNullOrWhiteSpace(parameter.Name)
                    ? ResolveTargetDisplayName(parameter.Target, tokens.Count)
                    : parameter.Name.Trim();

                if (parameter.Variadic && !parameterName.EndsWith("...", StringComparison.Ordinal)) {
                    parameterName += "...";
                }

                string wrapped = parameter.Optional
                    ? "[" + parameterName + "]"
                    : "<" + parameterName + ">";

                tokens.Add(new ArgumentDisplayToken(
                    Text: wrapped,
                    IsVariadic: parameter.Variadic));
            }

            return tokens.Count > 0;
        }

        private static bool TrySelectPatternForDisplay(
            ConsoleCommandHint hint,
            ReadLineCommandParse parse,
            out ConsoleCommandPatternHint pattern)
        {
            pattern = new ConsoleCommandPatternHint();
            if (hint.ParameterPatterns.Length == 0) {
                return false;
            }

            if (hint.ParameterPatterns.Length == 1) {
                pattern = hint.ParameterPatterns[0];
                return true;
            }

            List<ConsoleCommandPatternHint> matches = [];
            foreach (ConsoleCommandPatternHint candidate in hint.ParameterPatterns) {
                List<string> literals = [.. candidate.SubCommands
                    .Where(static sub => !string.IsNullOrWhiteSpace(sub))
                    .Select(static sub => sub.Trim())];

                if (!MatchPattern(parse, literals)) {
                    continue;
                }

                if (parse.ArgumentIndex >= 0 &&
                    parse.ArgumentIndex < literals.Count &&
                    !string.IsNullOrWhiteSpace(parse.CurrentToken) &&
                    !literals[parse.ArgumentIndex].StartsWith(parse.CurrentToken, StringComparison.OrdinalIgnoreCase)) {

                    continue;
                }

                matches.Add(candidate);
            }

            if (matches.Count == 1) {
                pattern = matches[0];
                return true;
            }

            ConsoleCommandPatternHint? fallback = hint.ParameterPatterns.FirstOrDefault(static item => item.SubCommands.Length == 0);
            if (fallback is not null && matches.Count == 0) {
                pattern = fallback;
                return true;
            }

            return false;
        }

        private static string ResolveTargetDisplayName(ReadLineTargetKey target, int fallbackIndex)
        {
            return target.Value switch {
                "player" => "player",
                "server" => "server",
                "item" => "item",
                "boolean" => "bool",
                "enum" => "enum",
                _ => "arg" + (fallbackIndex + 1),
            };
        }

        private ConsoleCommandHint? ResolveActiveCommandHint(
            ReadLineResolvedContext context,
            ReadLineCommandParse parse,
            int completionIndex,
            IReadOnlyList<string>? suggestions,
            string input)
        {
            if (context.Purpose != ConsoleInputPurpose.CommandLine || !parse.IsCommandToken) {
                return parse.Hint;
            }

            if (completionIndex > 0) {
                ConsoleCommandHint? selected = ResolveSuggestedCommandHint(
                    context,
                    completionIndex,
                    suggestions,
                    input,
                    allowFirstCandidateFallback: false);
                if (selected is not null) {
                    return selected;
                }
            }

            if (parse.Hint is not null) {
                return parse.Hint;
            }

            ConsoleCommandHint? suggested = ResolveSuggestedCommandHint(
                context,
                completionIndex,
                suggestions,
                input,
                allowFirstCandidateFallback: true);
            if (suggested is not null) {
                return suggested;
            }

            if (string.IsNullOrWhiteSpace(parse.CommandName)) {
                return null;
            }

            return context.CommandHints
                .SelectMany(static hint => hint.Aliases
                    .Append(hint.PrimaryName)
                    .Where(static name => !string.IsNullOrWhiteSpace(name))
                    .Select(name => new {
                        Hint = hint,
                        Name = name,
                    }))
                .Where(match => match.Name.StartsWith(parse.CommandName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(match => match.Name, StringComparer.OrdinalIgnoreCase)
                .Select(static match => match.Hint)
                .FirstOrDefault();
        }

        private ConsoleCommandHint? ResolveSuggestedCommandHint(
            ReadLineResolvedContext context,
            int completionIndex,
            IReadOnlyList<string>? suggestions,
            string input,
            bool allowFirstCandidateFallback)
        {
            IReadOnlyList<string> candidates = suggestions ?? ResolveCommandLineSuggestions(input, context);
            if (candidates.Count == 0) {
                return null;
            }

            string? selectedCandidate = null;
            int selectedIndex = completionIndex - 1;
            if (selectedIndex >= 0 && selectedIndex < candidates.Count) {
                selectedCandidate = candidates[selectedIndex];
            }
            else if (allowFirstCandidateFallback) {
                selectedCandidate = candidates[0];
            }

            if (string.IsNullOrWhiteSpace(selectedCandidate)) {
                return null;
            }

            ReadLineCommandParse selectedParse = ParseCommandInput(selectedCandidate, context);
            return selectedParse.Hint;
        }

        private static ParsedCommandBodyState ParseCommandBody(string commandBody, int bodyOffset)
        {
            List<ParsedCommandToken> tokens = [];
            StringBuilder current = new();
            bool inQuotes = false;
            int tokenStart = -1;
            int emptyQuotedTokenStart = -1;

            void CommitToken(bool quoted)
            {
                if (current.Length == 0) {
                    tokenStart = -1;
                    return;
                }

                tokens.Add(new ParsedCommandToken(
                    Value: current.ToString(),
                    StartIndex: tokenStart >= 0 ? tokenStart : bodyOffset,
                    Quoted: quoted));
                current.Clear();
                tokenStart = -1;
                emptyQuotedTokenStart = -1;
            }

            for (int index = 0; index < commandBody.Length; index++) {
                char currentChar = commandBody[index];

                if (currentChar == '\\' && index + 1 < commandBody.Length) {
                    char escaped = commandBody[++index];
                    if (escaped != '"' && escaped != ' ' && escaped != '\\') {
                        if (tokenStart < 0) {
                            tokenStart = bodyOffset + index - 1;
                        }
                        current.Append('\\');
                    }
                    else if (tokenStart < 0) {
                        tokenStart = bodyOffset + index - 1;
                    }

                    current.Append(escaped);
                    emptyQuotedTokenStart = -1;
                    continue;
                }

                if (currentChar == '"') {
                    if (inQuotes) {
                        CommitToken(quoted: true);
                        inQuotes = false;
                    }
                    else {
                        if (current.Length > 0) {
                            CommitToken(quoted: false);
                        }

                        inQuotes = true;
                        tokenStart = bodyOffset + index + 1;
                        emptyQuotedTokenStart = tokenStart;
                    }

                    continue;
                }

                if (IsTokenizerWhiteSpace(currentChar) && !inQuotes) {
                    CommitToken(quoted: false);
                    continue;
                }

                if (tokenStart < 0) {
                    tokenStart = bodyOffset + index;
                }

                current.Append(currentChar);
                emptyQuotedTokenStart = -1;
            }

            if (current.Length > 0) {
                CommitToken(quoted: inQuotes);
            }

            bool endsWithWhitespace = commandBody.Length > 0
                && IsTokenizerWhiteSpace(commandBody[^1])
                && !inQuotes;
            bool endsWithEmptyQuotedToken = inQuotes
                && current.Length == 0
                && emptyQuotedTokenStart >= 0;
            bool endsWithSpace = endsWithWhitespace || endsWithEmptyQuotedToken;

            if (tokens.Count == 0) {
                int currentTokenStart = endsWithEmptyQuotedToken ? emptyQuotedTokenStart : bodyOffset;
                return new ParsedCommandBodyState(
                    Tokens: tokens,
                    ArgumentTokens: [],
                    CurrentToken: endsWithEmptyQuotedToken ? string.Empty : commandBody,
                    CurrentTokenStart: currentTokenStart,
                    IsCommandToken: true,
                    ArgumentIndex: -1,
                    EndsWithSpace: endsWithSpace);
            }

            bool isCommandToken = tokens.Count == 1 && !endsWithSpace;
            if (isCommandToken) {
                ParsedCommandToken commandToken = tokens[0];
                return new ParsedCommandBodyState(
                    Tokens: tokens,
                    ArgumentTokens: [],
                    CurrentToken: commandToken.Value,
                    CurrentTokenStart: commandToken.StartIndex,
                    IsCommandToken: true,
                    ArgumentIndex: -1,
                    EndsWithSpace: false);
            }

            List<ParsedCommandToken> argumentTokens = [.. tokens.Skip(1)];
            string currentToken;
            int currentTokenStartIndex;
            int argumentIndex;

            if (endsWithSpace) {
                currentToken = string.Empty;
                currentTokenStartIndex = endsWithEmptyQuotedToken
                    ? emptyQuotedTokenStart
                    : bodyOffset + commandBody.Length;
                argumentIndex = Math.Max(0, argumentTokens.Count);
            }
            else {
                ParsedCommandToken token = tokens[^1];
                currentToken = token.Value;
                currentTokenStartIndex = token.StartIndex;
                argumentIndex = Math.Max(0, argumentTokens.Count - 1);
            }

            return new ParsedCommandBodyState(
                Tokens: tokens,
                ArgumentTokens: argumentTokens,
                CurrentToken: currentToken,
                CurrentTokenStart: currentTokenStartIndex,
                IsCommandToken: false,
                ArgumentIndex: argumentIndex,
                EndsWithSpace: endsWithSpace);
        }

        private static bool IsTokenizerWhiteSpace(char c)
        {
            return c == ' ' || c == '\t' || c == '\n';
        }

        private static int ResolveGlobalCompletionIndex(ReadLineReactiveState state)
        {
            if (state.CompletionIndex <= 0) {
                return 0;
            }

            return Math.Max(0, state.CandidateWindowOffset) + state.CompletionIndex;
        }

        private readonly record struct ArgumentDisplayToken(
            string Text,
            bool IsVariadic);

        private readonly record struct ParsedCommandToken(
            string Value,
            int StartIndex,
            bool Quoted);

        private readonly record struct ParsedCommandBodyState(
            IReadOnlyList<ParsedCommandToken> Tokens,
            IReadOnlyList<ParsedCommandToken> ArgumentTokens,
            string CurrentToken,
            int CurrentTokenStart,
            bool IsCommandToken,
            int ArgumentIndex,
            bool EndsWithSpace);

        private static IEnumerable<string> ResolveTargetCandidates(
            ReadLineCommandParse parse,
            ReadLineResolvedContext context,
            ReadLineTargetKey target)
        {
            if (target == ReadLineTargetKeys.Enum
                && TryResolvePatternParameter(parse, out ConsoleCommandParameterDescriptor? enumDescriptor)
                && enumDescriptor.EnumCandidates.Length > 0) {
                return enumDescriptor.EnumCandidates;
            }

            IReadOnlyList<string> resolved = context.ResolveCandidates(target)
                .Select(static item => item.Value)
                .ToList();
            if (resolved.Count > 0) {
                return resolved;
            }

            if (target != ReadLineTargetKeys.Plain) {
                return context.ResolveCandidates(ReadLineTargetKeys.Plain)
                    .Select(static item => item.Value)
                    .ToList();
            }

            return [];
        }

        private static IReadOnlyList<string> ResolvePatternLiteralSuggestions(ReadLineCommandParse parse)
        {
            if (parse.Hint is null || parse.ArgumentIndex < 0 || parse.Hint.ParameterPatterns.Length == 0) {
                return [];
            }

            List<string> completed = GetCompletedArgumentTokens(parse);
            HashSet<string> suggestions = new(StringComparer.OrdinalIgnoreCase);

            foreach (ConsoleCommandPatternHint pattern in parse.Hint.ParameterPatterns) {
                List<string> literals = [.. pattern.SubCommands
                    .Where(static sub => !string.IsNullOrWhiteSpace(sub))
                    .Select(static sub => sub.Trim())];

                if (parse.ArgumentIndex >= literals.Count) {
                    continue;
                }

                if (!MatchLiteralPrefix(completed, literals)) {
                    continue;
                }

                suggestions.Add(literals[parse.ArgumentIndex]);
            }

            return suggestions.OrderBy(static item => item, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static bool TryResolvePatternParameter(ReadLineCommandParse parse, out ConsoleCommandParameterDescriptor descriptor)
        {
            descriptor = new ConsoleCommandParameterDescriptor();

            if (parse.Hint is null || parse.ArgumentIndex < 0 || parse.Hint.ParameterPatterns.Length == 0) {
                return false;
            }

            foreach (ConsoleCommandPatternHint pattern in parse.Hint.ParameterPatterns) {
                List<string> literals = [.. pattern.SubCommands
                    .Where(static sub => !string.IsNullOrWhiteSpace(sub))
                    .Select(static sub => sub.Trim())];

                if (!MatchPattern(parse, literals)) {
                    continue;
                }

                int parameterIndex = parse.ArgumentIndex - literals.Count;
                if (parameterIndex < 0) {
                    continue;
                }

                if (parameterIndex < pattern.Parameters.Length) {
                    descriptor = pattern.Parameters[parameterIndex];
                    return true;
                }

                if (pattern.Parameters.Length == 0) {
                    continue;
                }

                ConsoleCommandParameterDescriptor tail = pattern.Parameters[^1];
                if (tail.Variadic) {
                    descriptor = tail;
                    return true;
                }
            }

            return false;
        }

        private static bool MatchPattern(ReadLineCommandParse parse, IReadOnlyList<string> literals)
        {
            if (literals.Count == 0) {
                return true;
            }

            List<string> completed = GetCompletedArgumentTokens(parse);
            if (completed.Count < literals.Count) {
                return MatchLiteralPrefix(completed, literals);
            }

            for (int index = 0; index < literals.Count; index++) {
                if (!completed[index].Equals(literals[index], StringComparison.OrdinalIgnoreCase)) {
                    return false;
                }
            }

            return true;
        }

        private static bool MatchLiteralPrefix(IReadOnlyList<string> completed, IReadOnlyList<string> literals)
        {
            if (completed.Count > literals.Count) {
                return false;
            }

            for (int index = 0; index < completed.Count; index++) {
                if (!completed[index].Equals(literals[index], StringComparison.OrdinalIgnoreCase)) {
                    return false;
                }
            }

            return true;
        }

        private static List<string> GetCompletedArgumentTokens(ReadLineCommandParse parse)
        {
            if (parse.ArgumentIndex < 0 || parse.ArgumentTokens.Count == 0) {
                return [];
            }

            int completedCount = parse.EndsWithSpace
                ? parse.ArgumentTokens.Count
                : Math.Max(0, parse.ArgumentTokens.Count - 1);

            return [.. parse.ArgumentTokens.Take(completedCount)];
        }

        private static IReadOnlyList<string> BuildCandidateList(
            string input,
            IEnumerable<string> candidates,
            string prefix,
            bool matchRawToken,
            bool preserveOrder = false)
        {
            List<string> results = [];
            string token = input ?? string.Empty;

            foreach (string candidate in candidates.Where(static c => !string.IsNullOrWhiteSpace(c))) {
                bool canUse = !matchRawToken || string.IsNullOrEmpty(token) || candidate.StartsWith(token, StringComparison.OrdinalIgnoreCase);
                if (!canUse) {
                    continue;
                }

                string renderedCandidate = matchRawToken
                    ? BuildRenderedCommandCandidate(candidate, prefix)
                    : candidate;
                string full = prefix + renderedCandidate;
                if (matchRawToken || string.IsNullOrEmpty(input) || full.StartsWith(prefix + token, StringComparison.OrdinalIgnoreCase)) {
                    results.Add(full);
                }
            }

            return preserveOrder
                ? DistinctPreserveOrder(results)
                : DistinctAndSort(results);
        }

        private static string BuildRenderedCommandCandidate(string candidate, string tokenPrefix)
        {
            bool prefixHasOpenQuote = tokenPrefix.EndsWith("\"", StringComparison.Ordinal);
            bool needsQuotes = prefixHasOpenQuote || RequiresQuotedCommandArgument(candidate);
            if (!needsQuotes) {
                return candidate;
            }

            string escaped = EscapeQuotedCommandArgument(candidate);
            if (prefixHasOpenQuote) {
                return escaped + '"';
            }

            return '"' + escaped + '"';
        }

        private static bool RequiresQuotedCommandArgument(string value)
        {
            if (string.IsNullOrEmpty(value)) {
                return false;
            }

            foreach (char c in value) {
                if (char.IsWhiteSpace(c) || c == '"' || c == '\\') {
                    return true;
                }
            }

            return false;
        }

        private static string EscapeQuotedCommandArgument(string value)
        {
            return value
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal);
        }

        private static IReadOnlyList<ReadLineSuggestion> OrderSuggestionItems(IEnumerable<ReadLineSuggestion> items)
        {
            List<ReadLineSuggestion> ordered = [.. items
                .Where(static item => !string.IsNullOrWhiteSpace(item.Value))
                .OrderByDescending(static item => item.Weight)
                .ThenBy(static item => item.Value, StringComparer.OrdinalIgnoreCase)];

            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            List<ReadLineSuggestion> unique = [];
            foreach (ReadLineSuggestion item in ordered) {
                if (!seen.Add(item.Value)) {
                    continue;
                }
                unique.Add(item);
            }

            return unique;
        }

        private static IReadOnlyList<string> DistinctPreserveOrder(IEnumerable<string> values)
        {
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            List<string> output = [];

            foreach (string value in values) {
                if (string.IsNullOrWhiteSpace(value)) {
                    continue;
                }

                if (!seen.Add(value)) {
                    continue;
                }

                output.Add(value);
            }

            return output;
        }

        private static IReadOnlyList<string> DistinctAndSort(IEnumerable<string> values)
        {
            return values
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}

