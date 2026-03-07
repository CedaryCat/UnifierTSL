using System.Collections.Immutable;
using UnifierTSL.ConsoleClient.Shared.ConsolePrompting;

namespace UnifierTSL.CLI.Prompting;

public sealed class AnnotatedConsolePromptProvider
{
    private readonly AnnotatedConsolePromptOptions options;

    public AnnotatedConsolePromptProvider(AnnotatedConsolePromptOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        ArgumentNullException.ThrowIfNull(options.CommandPrefixResolver);
        ArgumentNullException.ThrowIfNull(options.CommandSpecResolver);
        ArgumentNullException.ThrowIfNull(options.PlayerCandidateResolver);
        ArgumentNullException.ThrowIfNull(options.ServerCandidateResolver);
        ArgumentNullException.ThrowIfNull(options.ItemCandidateResolver);
    }

    public ConsolePromptSpec BuildContextSpec()
    {
        ImmutableArray<string> prefixes = BuildCommandPrefixes();
        ImmutableArray<ConsoleCommandSpec> commandSpecs = BuildCommandSpecs();

        ImmutableDictionary<ConsoleSuggestionKind, ImmutableArray<ConsoleSuggestion>> candidates =
            ImmutableDictionary<ConsoleSuggestionKind, ImmutableArray<ConsoleSuggestion>>.Empty
                .Add(ConsoleSuggestionKind.Player, BuildTargetCandidates(options.PlayerCandidateResolver))
                .Add(ConsoleSuggestionKind.Server, BuildTargetCandidates(options.ServerCandidateResolver))
                .Add(ConsoleSuggestionKind.Item, BuildTargetCandidates(options.ItemCandidateResolver))
                .Add(ConsoleSuggestionKind.Boolean, ConsoleSuggestionCatalog.DefaultBooleanSuggestions);

        return new ConsolePromptSpec {
            Purpose = ConsoleInputPurpose.CommandLine,
            Prompt = "cmd> ",
            CommandPrefixes = prefixes,
            CommandSpecs = commandSpecs,
            StaticCandidates = candidates,
            BaseStatusBodyLines = [
                GetString("use Tab/Shift+Tab to rotate, Right to accept"),
            ],
        };
    }

    private ImmutableArray<string> BuildCommandPrefixes()
    {
        ImmutableArray<string> prefixes = [.. ResolveSafe(options.CommandPrefixResolver)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)];

        return prefixes.Length == 0 ? ["/"] : prefixes;
    }

    private static ImmutableArray<ConsoleSuggestion> BuildTargetCandidates(Func<IReadOnlyList<string>> resolver)
    {
        return [.. ResolveSafe(resolver)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .Select(static value => new ConsoleSuggestion(value, 0))];
    }

    private ImmutableArray<ConsoleCommandSpec> BuildCommandSpecs()
    {
        Dictionary<string, ConsoleCommandSpec> specs = new(StringComparer.OrdinalIgnoreCase);

        foreach (ConsoleCommandSpec command in ResolveSafe(options.CommandSpecResolver)) {
            if (string.IsNullOrWhiteSpace(command.PrimaryName)) {
                continue;
            }

            string primary = command.PrimaryName.Trim();
            ImmutableArray<string> aliases = [.. command.Aliases
                .Where(static alias => !string.IsNullOrWhiteSpace(alias))
                .Where(alias => !alias.Equals(primary, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static alias => alias, StringComparer.OrdinalIgnoreCase)];

            specs[primary] = new ConsoleCommandSpec {
                PrimaryName = primary,
                Aliases = aliases,
                HelpText = command.HelpText ?? string.Empty,
                Patterns = NormalizePatterns(command.Patterns),
            };
        }

        return [.. specs.Values.OrderBy(static spec => spec.PrimaryName, StringComparer.OrdinalIgnoreCase)];
    }

    private static ImmutableArray<ConsoleCommandPatternSpec> NormalizePatterns(IEnumerable<ConsoleCommandPatternSpec> patterns)
    {
        return [.. patterns
            .Where(static pattern => pattern is not null)
            .Select(pattern => new ConsoleCommandPatternSpec {
                SubCommands = [.. pattern.SubCommands
                .Where(static sub => !string.IsNullOrWhiteSpace(sub))
                .Distinct(StringComparer.OrdinalIgnoreCase)],
                Parameters = [.. pattern.Parameters.Select(parameter => new ConsoleCommandParameterSpec {
                    Name = parameter.Name?.Trim() ?? string.Empty,
                    Kind = parameter.Kind,
                    Optional = parameter.Optional,
                    Variadic = parameter.Variadic,
                    EnumCandidates = [.. parameter.EnumCandidates
                        .Where(static item => !string.IsNullOrWhiteSpace(item))
                        .Select(static item => item.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)],
                })],
            })];
    }

    private static IReadOnlyList<T> ResolveSafe<T>(Func<IReadOnlyList<T>> resolver)
    {
        try {
            return resolver() ?? [];
        }
        catch {
            return [];
        }
    }
}
