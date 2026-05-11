using System.Collections.Immutable;
using UnifierTSL.Surface.Prompting;
using UnifierTSL.Surface.Prompting.Semantics;
using UnifierTSL.Commanding.Bindings;
using UnifierTSL.Commanding.Composition;
using UnifierTSL.Commanding.Endpoints;
using UnifierTSL.Surface.Prompting.Model;

namespace UnifierTSL.Commanding.Prompting;

public static class CommandPromptProjection
{
    public static IReadOnlyList<PromptAlternativeSpec> BuildAlternatives(
        CommandEndpointId endpointId,
        CommandRootDefinition root,
        IEnumerable<CommandActionDefinition> actions) {
        List<PromptAlternativeSpec> alternatives = [];
        foreach (var action in actions) {
            alternatives.AddRange(BuildAlternatives(endpointId, root, action));
        }

        return alternatives;
    }

    private static IEnumerable<PromptAlternativeSpec> BuildAlternatives(
        CommandEndpointId endpointId,
        CommandRootDefinition root,
        CommandActionDefinition action) {
        var canonicalCommand = BuildCanonicalCommandName(root.RootName, action.PathSegments);
        var summary = string.IsNullOrWhiteSpace(action.Summary)
            ? root.Summary
            : action.Summary;
        var alternativeId = BuildAlternativeId(endpointId, action);
        var displayGroupKey = BuildDisplayGroupKey(endpointId, root.RootName, action.PathSegments);

        foreach (var invocationName in ResolveInvocationNames(root)) {
            foreach (var invocationPath in EnumerateActionPaths(action)) {
                yield return new PromptAlternativeSpec {
                    AlternativeId = alternativeId,
                    DisplayGroupKey = displayGroupKey,
                    Title = BuildCanonicalCommandName(invocationName, invocationPath),
                    ResultDisplayText = canonicalCommand,
                    Summary = summary,
                    Metadata = new CommandPromptAlternativeMetadata {
                        EndpointId = endpointId,
                        RootName = root.RootName,
                        InvocationName = invocationName,
                        CanonicalCommand = canonicalCommand,
                        PathSegments = invocationPath,
                        ActionMethod = action.Method,
                        PromptRouteGuards = action.PromptRouteGuards,
                    },
                    OverflowBehavior = action.IgnoreTrailingArguments
                        ? PromptOverflowBehavior.IgnoreAdditionalTokens
                        : PromptOverflowBehavior.Error,
                    Segments = BuildSegments(invocationName, action, invocationPath),
                    Shims = BuildShims(action, invocationPath),
                };
            }
        }
    }

    private static ImmutableArray<PromptSegmentSpec> BuildSegments(
        string invocationName,
        CommandActionDefinition action,
        ImmutableArray<string> pathSegments) {
        List<PromptSegmentSpec> segments = [
            new PromptLiteralSegmentSpec {
                Name = "command",
                Value = invocationName,
                HighlightStyleId = PromptStyleKeys.SyntaxKeyword,
            },
        ];

        foreach (var pathSegment in pathSegments) {
            segments.Add(new PromptLiteralSegmentSpec {
                Name = pathSegment,
                Value = pathSegment,
                HighlightStyleId = PromptStyleKeys.SyntaxKeyword,
            });
        }

        foreach (var parameter in action.Parameters) {
            segments.Add(new PromptSlotSegmentSpec {
                Name = parameter.Name,
                DisplayLabel = parameter.Name,
                SemanticKey = parameter.SemanticKey,
                CompletionKindId = parameter.SuggestionKindId,
                Cardinality = ResolveCardinality(parameter),
                ValidationMode = parameter.ValidationMode,
                RouteMatchKind = parameter.RouteMatchKind,
                RouteConsumptionMode = parameter.RouteConsumptionMode,
                RouteAcceptedTokens = parameter.RouteAcceptedTokens,
                RouteSpecificity = parameter.RouteSpecificity,
                ExcludeCurrentContextFromCandidates = (parameter.Modifiers & CommandParamModifiers.ExcludeCurrentContext) != 0,
                EnumCandidates = parameter.EnumCandidates,
                AcceptedSpecialTokens = parameter.AcceptedSpecialTokens,
                Metadata = parameter.Metadata,
            });
        }

        return [.. segments];
    }

    private static ImmutableArray<PromptSyntaxShimSpec> BuildShims(CommandActionDefinition action, ImmutableArray<string> pathSegments) {
        if (action.FlagsParameter is null || action.FlagsParameter.Flags.Length == 0) {
            return [];
        }

        return [
            new PromptFlagSetShimSpec {
                StartSegmentIndex = 1 + pathSegments.Length,
                Options = [.. action.FlagsParameter.Flags.Select(static flag => new PromptModifierOptionSpec {
                    Key = string.IsNullOrWhiteSpace(flag.MemberName) ? flag.CanonicalToken : flag.MemberName,
                    CanonicalToken = flag.CanonicalToken,
                    Tokens = flag.Tokens,
                    DisplayLabel = string.IsNullOrWhiteSpace(flag.MemberName) ? flag.CanonicalToken : flag.MemberName,
                })],
            },
        ];
    }

    private static PromptSlotCardinality ResolveCardinality(CommandParamDefinition parameter) {
        if (parameter.Variadic) {
            return PromptSlotCardinality.Variadic;
        }

        return parameter.Optional
            ? PromptSlotCardinality.Optional
            : PromptSlotCardinality.Required;
    }

    private static IEnumerable<string> ResolveInvocationNames(CommandRootDefinition root) {
        yield return root.RootName;
        foreach (var alias in root.Aliases
            .Where(static alias => !string.IsNullOrWhiteSpace(alias))
            .Select(static alias => alias.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(alias => !alias.Equals(root.RootName, StringComparison.OrdinalIgnoreCase))) {
            yield return alias;
        }
    }

    private static string BuildCanonicalCommandName(string rootName, ImmutableArray<string> pathSegments) {
        return string.Join(' ', new[] { rootName }.Concat(pathSegments).Where(static segment => !string.IsNullOrWhiteSpace(segment)));
    }

    private static IEnumerable<ImmutableArray<string>> EnumerateActionPaths(CommandActionDefinition action) {
        yield return action.PathSegments;
        foreach (var alias in action.PathAliases) {
            if (alias.Any(static token => token.Any(char.IsWhiteSpace))) {
                continue;
            }

            yield return alias;
        }
    }

    private static string BuildAlternativeId(CommandEndpointId endpointId, CommandActionDefinition action) {
        var typeName = action.Method.DeclaringType?.FullName ?? action.Method.DeclaringType?.Name ?? "unknown-type";
        var signature = string.Join(
            ",",
            action.Method.GetParameters().Select(static parameter => parameter.ParameterType.FullName ?? parameter.ParameterType.Name));
        return $"command-alt:{endpointId.Value}:{typeName}.{action.Method.Name}({signature})";
    }

    private static string BuildDisplayGroupKey(CommandEndpointId endpointId, string rootName, ImmutableArray<string> pathSegments) {
        var normalizedPath = pathSegments.Length == 0
            ? string.Empty
            : ":" + string.Join(":", pathSegments.Select(static segment => segment.Trim()));
        return $"command:{endpointId.Value}:{rootName.Trim()}{normalizedPath}";
    }
}
