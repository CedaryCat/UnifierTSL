using System.Collections.Immutable;
using UnifierTSL.Surface.Prompting;
using UnifierTSL.Commanding;
using UnifierTSL.Surface.Prompting.Model;

namespace UnifierTSL.Surface.Prompting.Semantics;

public enum PromptOverflowBehavior : byte
{
    Error,
    IgnoreAdditionalTokens,
}

public enum PromptSlotCardinality : byte
{
    Required,
    Optional,
    Variadic,
}

public enum PromptSlotValidationMode : byte
{
    None,
    Integer,
}

public sealed record PromptSemanticSpec
{
    public string StatusLabel { get; init; } = "ctx";

    public string LiteralExpectationLabel { get; init; } = "literal";

    public string ModifierLabel { get; init; } = "modifiers";

    public string OverflowExpectationLabel { get; init; } = "no more input";

    public ImmutableArray<string> ActivationPrefixes { get; init; } = [];

    public ImmutableArray<PromptAlternativeSpec> Alternatives { get; init; } = [];
}

public sealed record PromptAlternativeSpec
{
    private readonly string generatedAlternativeId = $"prompt-alt:{Guid.NewGuid():N}";
    private string alternativeId = string.Empty;
    private string displayGroupKey = string.Empty;

    public string AlternativeId {
        get => string.IsNullOrWhiteSpace(alternativeId) ? generatedAlternativeId : alternativeId;
        init => alternativeId = NormalizeContractKey(value);
    }

    public string DisplayGroupKey {
        get => string.IsNullOrWhiteSpace(displayGroupKey)
            ? AlternativeId
            : displayGroupKey;
        init => displayGroupKey = NormalizeContractKey(value);
    }

    public string Title { get; init; } = string.Empty;

    public string ResultDisplayText { get; init; } = string.Empty;

    public string Summary { get; init; } = string.Empty;

    public PromptAlternativeMetadata? Metadata { get; init; }

    public PromptOverflowBehavior OverflowBehavior { get; init; } = PromptOverflowBehavior.Error;

    public ImmutableArray<PromptSegmentSpec> Segments { get; init; } = [];

    public ImmutableArray<PromptSyntaxShimSpec> Shims { get; init; } = [];

    private static string NormalizeContractKey(string? value) {
        return value?.Trim() ?? string.Empty;
    }
}

public abstract record PromptSegmentSpec
{
    public string Name { get; init; } = string.Empty;
}

public abstract record PromptAlternativeMetadata;

public sealed record PromptLiteralSegmentSpec : PromptSegmentSpec
{
    public string Value { get; init; } = string.Empty;

    public string HighlightStyleId { get; init; } = PromptStyleKeys.SyntaxKeyword;
}

public sealed record PromptSlotSegmentSpec : PromptSegmentSpec
{
    public string DisplayLabel { get; init; } = string.Empty;

    public SemanticKey? SemanticKey { get; init; }

    public string CompletionKindId { get; init; } = string.Empty;

    public PromptSlotCardinality Cardinality { get; init; } = PromptSlotCardinality.Required;

    public PromptSlotValidationMode ValidationMode { get; init; }

    public PromptRouteMatchKind RouteMatchKind { get; init; }

    public PromptRouteConsumptionMode RouteConsumptionMode { get; init; }

    public ImmutableArray<string> RouteAcceptedTokens { get; init; } = [];

    public int RouteSpecificity { get; init; }

    public bool ExcludeCurrentContextFromCandidates { get; init; }

    public ImmutableArray<string> EnumCandidates { get; init; } = [];

    public ImmutableArray<string> AcceptedSpecialTokens { get; init; } = [];

    public ImmutableArray<PromptSlotMetadataEntry> Metadata { get; init; } = [];

    public bool TryGetMetadataValue(string key, out string value) {
        return PromptSlotMetadata.TryGetValue(Metadata, key, out value);
    }
}

public sealed record PromptModifierOptionSpec
{
    public string Key { get; init; } = string.Empty;

    public string CanonicalToken { get; init; } = string.Empty;

    public ImmutableArray<string> Tokens { get; init; } = [];

    public string DisplayLabel { get; init; } = string.Empty;
}

public sealed record PromptNamedOptionSpec
{
    public string Key { get; init; } = string.Empty;

    public string CanonicalToken { get; init; } = string.Empty;

    public ImmutableArray<string> Tokens { get; init; } = [];

    public string SlotName { get; init; } = string.Empty;
}

public abstract record PromptSyntaxShimSpec
{
    public int StartSegmentIndex { get; init; }
}

public sealed record PromptModifierShimSpec : PromptSyntaxShimSpec
{
    public ImmutableArray<PromptModifierOptionSpec> Options { get; init; } = [];

    public bool ExcludeRecognizedModifiersFromSlotFlow { get; init; } = true;
}

public sealed record PromptFlagSetShimSpec : PromptSyntaxShimSpec
{
    public ImmutableArray<PromptModifierOptionSpec> Options { get; init; } = [];

    public bool ExcludeRecognizedFlagsFromSlotFlow { get; init; } = true;
}

public sealed record PromptNamedOptionShimSpec : PromptSyntaxShimSpec
{
    public ImmutableArray<PromptNamedOptionSpec> Options { get; init; } = [];
}

public readonly record struct PromptSemanticContext(
    PromptSemanticSpec Spec,
    PromptAlternativeSpec Alternative,
    PromptEditTarget EditTarget,
    string RawText);
