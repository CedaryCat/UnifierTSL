using System.Text.Json;
using UnifierTSL.CLI.Prompting;
using UnifierTSL.CLI.Status;
using UnifierTSL.ConsoleClient.Shared.ConsolePrompting;
using UnifierTSL.ConsoleClient.Shell;

namespace UnifierTSL.CLI;

public sealed class TerminalLauncherConsoleHost : ILauncherConsoleHost
{
    private readonly ConsoleShell shell = new();
    private readonly ConsoleStatusController statusController;
    private int disposed;
    private int vividStatusSupportState = -1;

    public TerminalLauncherConsoleHost() {
        statusController = new ConsoleStatusController(server: null, HandleStatusPublication);
        shell.UpdateTheme(ResolveStatusBarTheme());
    }

    public bool IsInteractive => shell.IsInteractive;

    public string ReadLine(ConsolePromptSpec contextSpec, bool trim = false) {
        ArgumentNullException.ThrowIfNull(contextSpec);
        ConsolePromptScenario scenario = ResolveShellScenario();
        ConsolePromptCompiler compiler = ConsolePromptRegistry.CreateCompiler(contextSpec, scenario, scenario);
        return ReadLineWithCompiler(compiler, trim);
    }

    public void WriteAnsi(string text) {
        shell.AppendLog(text, isAnsi: true);
    }

    public IDisposable BeginConsoleActivityStatus(string category, string message) {
        return statusController.BeginActivity(category, message);
    }

    public void Dispose() {
        if (Interlocked.Exchange(ref disposed, 1) != 0) {
            return;
        }

        try {
            statusController.Dispose();
        }
        catch {
        }

        shell.Dispose();
    }

    public void RefreshAppearanceSettings() {
        if (Volatile.Read(ref disposed) != 0) {
            return;
        }

        shell.UpdateTheme(ResolveStatusBarTheme());
        statusController.ReplayCurrent();
    }

    private string ReadLineWithCompiler(ConsolePromptCompiler compiler, bool trim) {
        ArgumentNullException.ThrowIfNull(compiler);

        ConsolePromptSessionRunner sessionRunner = new(compiler, ConsolePromptSessionRunner.LocalRenderOptions);
        ConsolePromptSessionState session = sessionRunner.Current;
        ConsoleRenderSnapshot initialRender = session.RenderSnapshot;
        string latestRenderJson = JsonSerializer.Serialize(initialRender);

        void PublishReactiveSnapshot(ConsoleInputState reactiveState) {
            try {
                ConsolePromptSessionState updated = sessionRunner.Update(reactiveState);
                string renderJson = JsonSerializer.Serialize(updated.RenderSnapshot);
                if (string.Equals(Volatile.Read(ref latestRenderJson), renderJson, StringComparison.Ordinal)) {
                    return;
                }

                Volatile.Write(ref latestRenderJson, renderJson);
                shell.UpdateReadLineContext(updated.RenderSnapshot);
            }
            catch (ObjectDisposedException) {
            }
        }

        void PublishRuntimeRefreshSnapshot() {
            try {
                if (!sessionRunner.TryRefreshRuntimeDependencies(out ConsolePromptSessionState refreshed)) {
                    return;
                }

                string renderJson = JsonSerializer.Serialize(refreshed.RenderSnapshot);
                if (string.Equals(Volatile.Read(ref latestRenderJson), renderJson, StringComparison.Ordinal)) {
                    return;
                }

                Volatile.Write(ref latestRenderJson, renderJson);
                shell.UpdateReadLineContext(refreshed.RenderSnapshot);
            }
            catch (ObjectDisposedException) {
            }
        }

        Timer? refreshTimer = null;
        int refreshStopped = 0;
        if (shell.IsInteractive && session.InputState.Purpose == ConsoleInputPurpose.CommandLine) {
            refreshTimer = new Timer(_ => {
                try {
                    if (Volatile.Read(ref refreshStopped) != 0) {
                        return;
                    }

                    PublishRuntimeRefreshSnapshot();
                }
                catch {
                }
            }, null, ConsoleStatusService.RefreshIntervalMs, ConsoleStatusService.RefreshIntervalMs);
        }

        string line;
        try {
            line = shell.ReadLine(
                initialRender,
                trim: false,
                onInputStateChanged: state => {
                    PublishReactiveSnapshot(state);
                });
        }
        finally {
            Interlocked.Exchange(ref refreshStopped, 1);
            try {
                refreshTimer?.Dispose();
            }
            catch {
            }
        }

        return trim ? line.Trim() : line;
    }

    private ConsolePromptScenario ResolveShellScenario() {
        return shell.IsInteractive
            ? ConsolePromptScenario.LocalInteractive
            : ConsolePromptScenario.NonInteractiveFallback;
    }

    private void HandleStatusPublication(ConsoleStatusPublication publication) {
        if (Volatile.Read(ref disposed) != 0) {
            return;
        }

        if (!publication.HasFrame || string.IsNullOrWhiteSpace(publication.Frame.Text)) {
            shell.ClearStatusFrame();
            return;
        }

        shell.UpdateStatusFrame(
            publication.Sequence,
            publication.Frame.Text,
            publication.Frame.IndicatorFrameIntervalMs,
            publication.Frame.IndicatorStylePrefix,
            publication.Frame.IndicatorFrames);
    }

    private ConsolePromptTheme ResolveStatusBarTheme() {
        ConsolePromptTheme theme = UnifierApi.GetConsolePromptTheme();
        bool interactive = shell.IsInteractive;
        bool supportsVividStatusBar = interactive && shell.SupportsVirtualTerminal;
        int state = !interactive
            ? 0
            : theme.UseVividStatusBar
            ? (supportsVividStatusBar ? 1 : 2)
            : 0;
        int previous = Interlocked.Exchange(ref vividStatusSupportState, state);
        if (state == 2 && previous != 2) {
            UnifierApi.Logger.Warning(
                GetString("launcher.colorfulConsoleStatus is enabled, but the current terminal does not support vivid ANSI status rendering. Falling back to the plain status bar palette."),
                category: "Console");
        }

        return theme;
    }
}
