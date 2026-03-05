using System.Collections.Immutable;
using UnifierTSL.CLI.Sessions;
using UnifierTSL.ConsoleClient.Shell;

namespace UnifierTSL.CLI.CommandHint;

public sealed class AnnotatedCommandLineHintProvider
{
    private readonly AnnotatedCommandHintOptions options;

    public AnnotatedCommandLineHintProvider(AnnotatedCommandHintOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        ArgumentNullException.ThrowIfNull(options.CommandPrefixResolver);
        ArgumentNullException.ThrowIfNull(options.CommandResolver);
        ArgumentNullException.ThrowIfNull(options.PlayerCandidateResolver);
        ArgumentNullException.ThrowIfNull(options.ServerCandidateResolver);
        ArgumentNullException.ThrowIfNull(options.ItemCandidateResolver);
        ArgumentNullException.ThrowIfNull(options.AnnotationResolver);
    }

    public ReadLineContextSpec BuildContextSpec()
    {
        ImmutableArray<string> prefixes = BuildCommandPrefixes();
        ImmutableArray<ConsoleCommandHint> commandHints = BuildCommandHints();

        ImmutableDictionary<ReadLineTargetKey, ImmutableArray<ReadLineSuggestion>> candidates =
            ImmutableDictionary<ReadLineTargetKey, ImmutableArray<ReadLineSuggestion>>.Empty
                .Add(ReadLineTargetKeys.Player, BuildTargetCandidates(options.PlayerCandidateResolver))
                .Add(ReadLineTargetKeys.Server, BuildTargetCandidates(options.ServerCandidateResolver))
                .Add(ReadLineTargetKeys.Item, BuildTargetCandidates(options.ItemCandidateResolver))
                .Add(ReadLineTargetKeys.Boolean, ReadLineTargetKeys.DefaultBooleanSuggestions);

        return new ReadLineContextSpec {
            Purpose = ConsoleInputPurpose.CommandLine,
            Prompt = "cmd> ",
            StatusPanelHeight = 4,
            AllowAnsiStatusEscapes = options.AllowAnsiStatusEscapes,
            CommandPrefixes = prefixes,
            CommandHints = commandHints,
            StaticCandidates = candidates,
            BaseStatusLines = [
                "use Tab/Shift+Tab to rotate, Right to accept",
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

    private static ImmutableArray<ReadLineSuggestion> BuildTargetCandidates(Func<IReadOnlyList<string>> resolver)
    {
        return [.. ResolveSafe(resolver)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .Select(static value => new ReadLineSuggestion(value, 0))];
    }

    private ImmutableArray<ConsoleCommandHint> BuildCommandHints()
    {
        Dictionary<string, ConsoleCommandHint> hints = new(StringComparer.OrdinalIgnoreCase);
        IReadOnlyDictionary<string, ConsoleCommandAnnotation> annotationIndex = BuildAnnotationIndex();

        foreach (ConsoleCommandRuntimeDescriptor command in ResolveSafe(options.CommandResolver)) {
            if (string.IsNullOrWhiteSpace(command.PrimaryName)) {
                continue;
            }

            string primary = command.PrimaryName.Trim();
            if (hints.ContainsKey(primary)) {
                continue;
            }

            ImmutableArray<string> aliases = [.. command.Aliases
                .Where(static alias => !string.IsNullOrWhiteSpace(alias))
                .Where(alias => !alias.Equals(primary, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static alias => alias, StringComparer.OrdinalIgnoreCase)];

            ConsoleCommandAnnotation? annotation = ResolveAnnotation(primary, aliases, annotationIndex);
            ImmutableArray<ConsoleCommandPatternHint> patterns = BuildPatternHints(annotation);

            hints[primary] = new ConsoleCommandHint {
                PrimaryName = primary,
                Aliases = aliases,
                HelpText = command.HelpText ?? string.Empty,
                ParameterHint = annotation?.ParameterHint ?? string.Empty,
                ParameterTargets = BuildTargetFallback(annotation),
                ParameterPatterns = patterns,
            };
        }

        return [.. hints.Values.OrderBy(static hint => hint.PrimaryName, StringComparer.OrdinalIgnoreCase)];
    }

    private static ImmutableArray<ConsoleCommandPatternHint> BuildPatternHints(ConsoleCommandAnnotation? annotation)
    {
        if (annotation is null || annotation.Patterns.Count == 0) {
            return [];
        }

        return [.. annotation.Patterns.Select(pattern => new ConsoleCommandPatternHint {
            SubCommands = [.. pattern.SubCommands
                .Where(static sub => !string.IsNullOrWhiteSpace(sub))
                .Distinct(StringComparer.OrdinalIgnoreCase)],
            Parameters = [.. pattern.Parameters.Select(parameter => new ConsoleCommandParameterDescriptor {
                Name = parameter.Name,
                Target = parameter.Target,
                Optional = parameter.Optional,
                Variadic = parameter.Variadic,
                EnumCandidates = [.. parameter.EnumCandidates
                    .Where(static item => !string.IsNullOrWhiteSpace(item))
                    .Distinct(StringComparer.OrdinalIgnoreCase)],
            })],
        })];
    }

    private static ImmutableArray<ReadLineTargetKey> BuildTargetFallback(ConsoleCommandAnnotation? annotation)
    {
        if (annotation is null || annotation.Patterns.Count == 0) {
            return [];
        }

        ConsoleCommandPatternAnnotation pattern = annotation.Patterns.FirstOrDefault(static p => p.SubCommands.Count == 0)
            ?? annotation.Patterns[0];

        return [.. pattern.Parameters.Select(static parameter => parameter.Target)];
    }

    private IReadOnlyDictionary<string, ConsoleCommandAnnotation> BuildAnnotationIndex()
    {
        Dictionary<string, ConsoleCommandAnnotation> index = new(StringComparer.OrdinalIgnoreCase);
        foreach (ConsoleCommandAnnotation annotation in ResolveSafe(options.AnnotationResolver)) {
            if (!string.IsNullOrWhiteSpace(annotation.PrimaryName)) {
                index[annotation.PrimaryName] = annotation;
            }

            foreach (string alias in annotation.Aliases.Where(static item => !string.IsNullOrWhiteSpace(item))) {
                index[alias] = annotation;
            }
        }

        return index;
    }

    private static ConsoleCommandAnnotation? ResolveAnnotation(
        string primary,
        IEnumerable<string> aliases,
        IReadOnlyDictionary<string, ConsoleCommandAnnotation> annotationIndex)
    {
        if (annotationIndex.TryGetValue(primary, out ConsoleCommandAnnotation? direct)) {
            return direct;
        }

        foreach (string alias in aliases) {
            if (annotationIndex.TryGetValue(alias, out ConsoleCommandAnnotation? byAlias)) {
                return byAlias;
            }
        }

        return null;
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
