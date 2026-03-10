using UnifierTSL.Servers;

namespace UnifierTSL.CLI.Prompting;

public enum ConsoleParameterExplainState : byte
{
    None,
    Resolved,
    Ambiguous,
    Invalid,
}

public readonly record struct ConsoleParameterExplainContext(
    ConsolePromptResolveContext ResolveContext,
    ServerContext? Server,
    ConsoleCommandSpec ActiveCommand,
    ConsoleCommandPatternSpec ActivePattern,
    ConsoleCommandParameterSpec ActiveParameter,
    int ArgumentIndex,
    string RawToken);

public readonly record struct ConsoleParameterExplainResult(
    ConsoleParameterExplainState State,
    string DisplayText)
{
    public static ConsoleParameterExplainResult None { get; } =
        new(ConsoleParameterExplainState.None, string.Empty);
}

public interface IConsoleParameterValueExplainer
{
    bool TryExplain(ConsoleParameterExplainContext context, out ConsoleParameterExplainResult result);
}
