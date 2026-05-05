using UnifierTSL.Surface.Activities;
using UnifierTSL.Surface.Prompting;

namespace UnifierTSL.Surface.Hosting;

public interface ILauncherSurfaceHost : IDisposable
{
    bool IsInteractive { get; }

    string ReadLine(PromptSurfaceSpec contextSpec, bool trim = false);

    ConsoleKeyInfo ReadKey(bool intercept);

    bool IsKeyAvailable();

    void Write(string text);

    void WriteAnsi(string text);

    IDisposable BeginSurfaceActivityStatus(string category, string message);

    void RefreshAppearanceSettings();
}

public interface ITrackedLauncherSurfaceActivityHost
{
    bool HasActiveSurfaceActivity { get; }

    ActivityHandle BeginTrackedSurfaceActivityStatus(
        string category,
        string message,
        ActivityDisplayOptions display = default,
        CancellationToken cancellationToken = default);
}
