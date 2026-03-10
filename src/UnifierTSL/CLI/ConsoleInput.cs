using UnifierTSL.CLI.Prompting;

namespace UnifierTSL.CLI;

internal static class ConsoleInput
{
    private static readonly Lock hostSync = new();
    private static readonly SemaphoreSlim readSync = new(1, 1);
    private static ILauncherConsoleHost host = new TerminalLauncherConsoleHost();

    public static bool IsInteractive {
        get {
            lock (hostSync) {
                return host.IsInteractive;
            }
        }
    }

    public static void ConfigureHost(ILauncherConsoleHost value) {
        ArgumentNullException.ThrowIfNull(value);

        readSync.Wait();
        try {
            ILauncherConsoleHost previous = host;
            lock (hostSync) {
                host = value;
            }
            if (!ReferenceEquals(previous, value)) {
                previous.Dispose();
            }
            RefreshAppearanceSettings();
        }
        finally {
            readSync.Release();
        }
    }

    public static string ReadLine(ConsolePromptSpec contextSpec, bool trim = false) {
        readSync.Wait();
        try {
            return GetHost().ReadLine(contextSpec, trim: trim);
        }
        finally {
            readSync.Release();
        }
    }

    public static string ReadLine(string? prompt = null, bool trim = false) {
        ConsolePromptSpec contextSpec = ConsolePromptSpec.CreatePlain(prompt);
        return ReadLine(contextSpec, trim);
    }

    public static void WriteAnsi(string text) {
        GetHost().WriteAnsi(text);
    }

    public static IDisposable BeginConsoleActivityStatus(string category, string message) {
        return GetHost().BeginConsoleActivityStatus(category, message);
    }

    internal static void RefreshAppearanceSettings() {
        GetHost().RefreshAppearanceSettings();
    }

    private static ILauncherConsoleHost GetHost() {
        lock (hostSync) {
            return host;
        }
    }
}
