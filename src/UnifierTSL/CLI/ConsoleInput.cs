using UnifierTSL.CLI.Sessions;
using UnifierTSL.Servers;

namespace UnifierTSL.CLI;

internal static class ConsoleInput
{
    private static readonly Lock frontendSync = new();
    private static readonly SemaphoreSlim readSync = new(1, 1);
    private static ILauncherConsoleFrontend frontend = new TerminalLauncherConsoleFrontend();

    public static bool IsInteractive {
        get {
            lock (frontendSync) {
                return frontend.IsInteractive;
            }
        }
    }

    public static void ConfigureFrontend(ILauncherConsoleFrontend value) {
        ArgumentNullException.ThrowIfNull(value);

        readSync.Wait();
        try {
            ILauncherConsoleFrontend previous = frontend;
            lock (frontendSync) {
                frontend = value;
            }
            if (!ReferenceEquals(previous, value)) {
                previous.Dispose();
            }
        }
        finally {
            readSync.Release();
        }
    }

    public static string ReadLine(ReadLineContextSpec contextSpec, bool trim = false) {
        readSync.Wait();
        try {
            return GetFrontend().ReadLine(contextSpec, trim: trim);
        }
        finally {
            readSync.Release();
        }
    }

    public static string ReadLine(string? prompt = null, bool trim = false) {
        ReadLineContextSpec contextSpec = ReadLineContextSpec.CreatePlain(prompt);
        return ReadLine(contextSpec, trim);
    }

    public static string ReadCommandLine(ServerContext? server = null) {
        readSync.Wait();
        try {
            return GetFrontend().ReadCommandLine(server);
        }
        finally {
            readSync.Release();
        }
    }

    public static void WriteAnsi(string text) {
        GetFrontend().WriteAnsi(text);
    }

    public static void WritePlain(string text) {
        GetFrontend().WritePlain(text);
    }

    public static IDisposable BeginTimedWorkStatus(string category, string message) {
        return GetFrontend().BeginTimedWorkStatus(category, message);
    }

    private static ILauncherConsoleFrontend GetFrontend() {
        lock (frontendSync) {
            return frontend;
        }
    }
}
