using System.Collections.Immutable;
using UnifierTSL.Servers;

namespace UnifierTSL.Surface.Prompting.Semantics;

public enum PromptRouteMatchKind : byte
{
    FreeText,
    ExactTokenSet,
    Integer,
    Boolean,
    TimeOnly,
    SemanticLookupSoft,
    SemanticLookupExact,
}

public enum PromptRouteConsumptionMode : byte
{
    SingleToken,
    GreedyPhrase,
    RemainingText,
    Variadic,
}

public enum PromptRouteGuardState : byte
{
    Unknown,
    Allow,
    Deny,
}

public readonly record struct PromptRouteGuardBucket(
    PromptRouteGuardState State,
    string Key,
    string Label)
{
    public static PromptRouteGuardBucket Empty { get; } = new(
        PromptRouteGuardState.Unknown,
        string.Empty,
        string.Empty);
}

public readonly record struct PromptRouteGuardEvaluationContext(
    PromptAlternativeSpec Alternative,
    string InputText,
    ImmutableArray<PromptInputToken> Tokens,
    bool EndsWithSpace,
    ServerContext? Server);

public interface IPromptRouteGuardBucketSource
{
    PromptRouteGuardBucket EvaluatePromptRouteGuardBucket(PromptRouteGuardEvaluationContext context);
}
