using System.Collections.Immutable;
using UnifierTSL.Surface.Prompting;
using UnifierTSL.Surface.Prompting.Semantics;
using UnifierTSL.Servers;

namespace UnifierTSL.Commanding.Prompting;

public readonly record struct CommandPromptRouteGuardContext(
    CommandPromptAlternativeMetadata Metadata,
    PromptAlternativeSpec Alternative,
    string InputText,
    ImmutableArray<PromptInputToken> Tokens,
    ImmutableArray<PromptInputToken> UserArguments,
    bool EndsWithSpace,
    ServerContext? Server);

public interface ICommandPromptRouteGuard
{
    string Key { get; }

    string Label { get; }

    PromptRouteGuardState Evaluate(CommandPromptRouteGuardContext context);
}

public interface ICommandPromptRouteGuardSource
{
    ICommandPromptRouteGuard CreatePromptRouteGuard();
}

public static class CommandPromptRouteGuard
{
    public static ICommandPromptRouteGuard Create(
        string key,
        string label,
        Func<CommandPromptRouteGuardContext, PromptRouteGuardState> evaluate) {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(evaluate);
        return new DelegateCommandPromptRouteGuard(key.Trim(), label?.Trim() ?? string.Empty, evaluate);
    }

    private sealed record DelegateCommandPromptRouteGuard(
        string Key,
        string Label,
        Func<CommandPromptRouteGuardContext, PromptRouteGuardState> EvaluateFunc) : ICommandPromptRouteGuard
    {
        public PromptRouteGuardState Evaluate(CommandPromptRouteGuardContext context) {
            return EvaluateFunc(context);
        }
    }
}
