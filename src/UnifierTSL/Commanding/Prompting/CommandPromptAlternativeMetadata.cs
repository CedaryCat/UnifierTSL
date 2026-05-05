using System.Collections.Immutable;
using System.Reflection;
using UnifierTSL.Surface.Prompting;
using UnifierTSL.Surface.Prompting.Semantics;
using UnifierTSL.Commanding.Endpoints;

namespace UnifierTSL.Commanding.Prompting;

public sealed record CommandPromptAlternativeMetadata : PromptAlternativeMetadata, IPromptRouteGuardBucketSource
{
    public required CommandEndpointId EndpointId { get; init; }

    public string RootName { get; init; } = string.Empty;

    public string InvocationName { get; init; } = string.Empty;

    public string CanonicalCommand { get; init; } = string.Empty;

    public ImmutableArray<string> PathSegments { get; init; } = [];

    public MethodInfo? ActionMethod { get; init; }

    public ImmutableArray<ICommandPromptRouteGuard> PromptRouteGuards { get; init; } = [];

    public PromptRouteGuardBucket EvaluatePromptRouteGuardBucket(PromptRouteGuardEvaluationContext context) {
        if (PromptRouteGuards.Length == 0) {
            return PromptRouteGuardBucket.Empty;
        }

        var fixedTokenCount = (string.IsNullOrWhiteSpace(InvocationName) ? 0 : 1) + PathSegments.Length;
        ImmutableArray<PromptInputToken> userArguments = context.Tokens.Length <= fixedTokenCount
            ? []
            : [.. context.Tokens.Skip(fixedTokenCount)];
        CommandPromptRouteGuardContext guardContext = new(
            Metadata: this,
            Alternative: context.Alternative,
            InputText: context.InputText,
            Tokens: context.Tokens,
            UserArguments: userArguments,
            EndsWithSpace: context.EndsWithSpace,
            Server: context.Server);
        List<(ICommandPromptRouteGuard Guard, PromptRouteGuardState State)> evaluatedGuards = [];
        var aggregateState = PromptRouteGuardState.Unknown;

        foreach (var guard in PromptRouteGuards) {
            var state = guard.Evaluate(guardContext);
            evaluatedGuards.Add((guard, state));
            if (state == PromptRouteGuardState.Deny) {
                aggregateState = PromptRouteGuardState.Deny;
                continue;
            }

            if (aggregateState != PromptRouteGuardState.Deny && state == PromptRouteGuardState.Allow) {
                aggregateState = PromptRouteGuardState.Allow;
            }
        }

        var key = string.Join("|", evaluatedGuards.Select(static item => item.Guard.Key + ":" + item.State));
        var label = string.Join(
            ", ",
            evaluatedGuards
                .Select(static item => item.Guard.Label)
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase));
        return new PromptRouteGuardBucket(aggregateState, key, label);
    }
}
