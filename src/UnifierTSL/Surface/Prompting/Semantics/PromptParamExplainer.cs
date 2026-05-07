using System.Diagnostics.CodeAnalysis;
using UnifierTSL.Surface.Prompting.Model;
using UnifierTSL.Servers;

namespace UnifierTSL.Surface.Prompting.Semantics;

/*
    These interfaces bridge prompt semantics to domain-specific binders.

    Candidate providers are allowed to expose raw display values only. Some domains, however,
    do not use plain ordinal StartsWith matching at runtime. A downstream binder may delegate
    to wildcard-aware lookup semantics, so "_" can be meaningful input rather than a literal.

    IParamValueCandidateMatcher exists so prompt filtering can mirror runtime binding without
    forcing UTSL core to understand every downstream syntax quirk. Removing this split and
    collapsing back to a single StartsWith rule will reintroduce silent prompt/runtime drift.

    Preserve local comments when editing this contract. If provider and matcher responsibilities
    change, update both the code and the explanation together so later refactors do not erase
    domain-specific matching by accident.
*/
public enum PromptParamExplainState : byte
{
    None,
    Resolved,
    Ambiguous,
    Invalid,
}

public readonly record struct PromptParamExplainContext(
    PromptResolveContext ResolveContext,
    ServerContext? Server,
    PromptAlternativeSpec ActiveAlternative,
    PromptSlotSegmentSpec ActiveSlot,
    PromptEditTarget EditTarget,
    string RawText)
{
    public string RawToken => RawText;
}

public readonly record struct PromptParamExplainResult(
    PromptParamExplainState State,
    string DisplayText,
    string DetailText = "")
{
    public static PromptParamExplainResult None { get; } = new(PromptParamExplainState.None, string.Empty);

    public static PromptParamExplainResult Resolved(string? displayText) {
        return string.IsNullOrWhiteSpace(displayText)
            ? Invalid()
            : new PromptParamExplainResult(PromptParamExplainState.Resolved, displayText.Trim());
    }

    public static PromptParamExplainResult Ambiguous(string? detailText = null) {
        return new PromptParamExplainResult(
            PromptParamExplainState.Ambiguous,
            string.Empty,
            detailText?.Trim() ?? string.Empty);
    }

    public static PromptParamExplainResult Invalid(string? detailText = null) {
        return new PromptParamExplainResult(
            PromptParamExplainState.Invalid,
            string.Empty,
            detailText?.Trim() ?? string.Empty);
    }

    public string FormatDisplayText() {
        if (State == PromptParamExplainState.Resolved) {
            return DisplayText?.Trim() ?? string.Empty;
        }

        var label = State switch {
            PromptParamExplainState.Ambiguous => GetString("ambiguous"),
            PromptParamExplainState.Invalid => GetString("invalid"),
            _ => string.Empty,
        };
        if (string.IsNullOrWhiteSpace(label)) {
            return string.Empty;
        }

        var detail = DetailText?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(detail)
            ? label
            : GetString("{0}: {1}", label, detail);
    }

    public string ToDiagnosticText() {
        return State == PromptParamExplainState.Invalid
            ? FormatDisplayText()
            : string.Empty;
    }

    public static bool TryParseInvalidDiagnostic(string? diagnostic, out string detail) {
        detail = string.Empty;
        if (string.IsNullOrWhiteSpace(diagnostic)) {
            return false;
        }

        var trimmed = diagnostic.Trim();
        var label = GetString("invalid");
        if (trimmed.Equals(label, StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        var prefix = label + ": ";
        if (!trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        detail = trimmed[prefix.Length..].Trim();
        return true;
    }
}

public readonly record struct PromptParamCandidateContext(
    PromptResolveContext ResolveContext,
    ServerContext? Server,
    PromptAlternativeSpec ActiveAlternative,
    PromptSlotSegmentSpec ActiveSlot,
    PromptEditTarget EditTarget,
    string RawText,
    string RawInputText)
{
    public string RawToken => RawText;
    public string RawInput => RawInputText;
}

public interface IParamValueExplainer
{
    long GetRevision(PromptParamExplainContext context);

    bool TryExplain(PromptParamExplainContext context, out PromptParamExplainResult result);
}

public interface IParamValueCandidateProvider
{
    long GetRevision(PromptParamCandidateContext context);

    IReadOnlyList<string> GetCandidates(PromptParamCandidateContext context);
}

public interface IParamValueCandidateMatcher
{
    // Return null to exclude the candidate entirely for the current raw token. This is not a
    // secondary sort hook; it is the prompt-side equivalent of "the runtime binder would not
    // consider this candidate a match right now".
    int? ResolveMatchWeight(PromptParamCandidateContext context, string candidate, int baseWeight);
}

public interface IParamValueNestedPromptProvider
{
    bool TryCreateNestedPrompt(PromptParamCandidateContext context, [NotNullWhen(true)] out PromptSemanticSpec? prompt);
}
