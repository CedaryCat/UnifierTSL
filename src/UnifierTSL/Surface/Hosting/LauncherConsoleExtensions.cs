using UnifierTSL.Surface.Activities;
using UnifierTSL.Surface.Prompting;

namespace UnifierTSL.Surface.Hosting;

public static class LauncherConsoleExtensions
{
    extension(Console)
    {
        public static bool IsInteractive => LauncherSurfaceConsole.IsInteractive;

        public static bool UseColorfulStatus => LauncherSurfaceConsole.UseColorfulStatus;

        public static bool HasActiveSurfaceActivity => LauncherSurfaceConsole.HasActiveSurfaceActivity();

        public static string ReadLine(PromptSurfaceSpec prompt, bool trim = false) {
            return LauncherSurfaceConsole.ReadLine(prompt, trim);
        }

        public static void WriteAnsi(string text) {
            LauncherSurfaceConsole.WriteAnsi(text);
        }

        public static SurfaceActivityScope BeginSurfaceActivity(
            string category,
            string message,
            ActivityDisplayOptions display = default,
            CancellationToken cancellationToken = default) {
            return LauncherSurfaceConsole.BeginSurfaceActivityScope(category, message, display, cancellationToken);
        }
    }
}
