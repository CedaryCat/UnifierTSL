using UnifiedServerProcess;
using UnifierTSL.CLI.Prompting;

namespace UnifierTSL.CLI;

public static class ConsolePromptInput
{
    /// <summary>
    /// Reads a line with an explicit prompt specification. Prefer this API for normal prompt-aware input flows.
    /// </summary>
    public static string? ReadLine(ConsoleSystemContext console, ConsolePromptSpec prompt, bool trim = false) {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(prompt);
        return ResolveRemoteConsole(console).ReadLine(prompt, trim);
    }

    internal static RemoteConsoleService ResolveRemoteConsole(ConsoleSystemContext console) {
        ArgumentNullException.ThrowIfNull(console);
        return console as RemoteConsoleService
            ?? throw new NotSupportedException(
                $"Console prompt input is only supported by '{typeof(RemoteConsoleService).FullName}'. Actual type: {console.GetType().FullName}.");
    }
}
