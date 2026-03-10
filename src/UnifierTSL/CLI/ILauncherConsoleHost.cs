using UnifierTSL.CLI.Prompting;

namespace UnifierTSL.CLI;

public interface ILauncherConsoleHost : IDisposable
{
    bool IsInteractive { get; }

    string ReadLine(ConsolePromptSpec contextSpec, bool trim = false);

    void WriteAnsi(string text);

    IDisposable BeginConsoleActivityStatus(string category, string message);

    void RefreshAppearanceSettings();
}
