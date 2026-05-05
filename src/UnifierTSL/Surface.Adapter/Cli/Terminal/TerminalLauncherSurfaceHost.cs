using UnifierTSL.Surface.Activities;
using UnifierTSL.Surface.Hosting;
using UnifierTSL.Surface.Prompting;
using UnifierTSL.Surface.Status;
using UnifierTSL.Surface.Prompting.Model;
using UnifierTSL.Contracts.Projection;
using UnifierTSL.Terminal;
using UnifierTSL.Terminal.Shell;

namespace UnifierTSL.Surface.Adapter.Cli.Terminal;

public sealed class TerminalLauncherSurfaceHost : ILauncherSurfaceHost, ITrackedLauncherSurfaceActivityHost
{
    private readonly ConsoleShell shell;
    private readonly LocalPromptReadInteractionDriver readSessionDriver;
    private readonly StatusProjectionRuntime statusRuntime;
    private int disposed;
    private long latestStatusSequence = -1;

    public TerminalLauncherSurfaceHost() {
        shell = new ConsoleShell(LauncherSurfaceConsole.InterceptionBridge);
        statusRuntime = new StatusProjectionRuntime(
            server: null,
            HandleStatusPublication);
        readSessionDriver = new LocalPromptReadInteractionDriver(
            shell,
            delta => statusRuntime.TrySelectRelativeActivity(delta));
        Console.CancelKeyPress += HandleCancelKeyPress;
    }

    public bool IsInteractive => shell.IsInteractive;

    public string ReadLine(PromptSurfaceSpec contextSpec, bool trim = false) {
        ArgumentNullException.ThrowIfNull(contextSpec);
        if (!shell.IsInteractive) {
            return ReadPlainLine(ResolvePromptLabel(contextSpec.Content), trim);
        }

        return readSessionDriver.ReadLine(contextSpec, trim);
    }

    public ConsoleKeyInfo ReadKey(bool intercept) {
        return shell.ReadKey(intercept);
    }

    public bool IsKeyAvailable() {
        return shell.IsKeyAvailable();
    }

    public bool HasActiveSurfaceActivity => statusRuntime.HasActiveActivity;

    public void Write(string text) {
        if (string.IsNullOrEmpty(text)) {
            return;
        }

        shell.AppendLog(text, isAnsi: false);
    }

    public void WriteAnsi(string text) {
        shell.AppendLog(text, isAnsi: true);
    }

    public IDisposable BeginSurfaceActivityStatus(string category, string message) {
        return BeginTrackedSurfaceActivityStatus(category, message);
    }

    public ActivityHandle BeginTrackedSurfaceActivityStatus(
        string category,
        string message,
        ActivityDisplayOptions display = default,
        CancellationToken cancellationToken = default) {
        return statusRuntime.BeginActivity(category, message, display, cancellationToken);
    }

    public void Dispose() {
        if (Interlocked.Exchange(ref disposed, 1) != 0) {
            return;
        }

        Console.CancelKeyPress -= HandleCancelKeyPress;
        try {
            statusRuntime.Dispose();
        }
        catch {
        }

        shell.Dispose();
    }

    public void RefreshAppearanceSettings() {
        if (Volatile.Read(ref disposed) != 0) {
            return;
        }

        statusRuntime.RepublishCurrent();
    }

    private void HandleCancelKeyPress(object? sender, ConsoleCancelEventArgs args) {
        args.Cancel = true;
        if (Volatile.Read(ref disposed) != 0) {
            return;
        }

        var result = statusRuntime.TryCancelSelectedActivity(out var activity);
        if (!activity.HasValue) {
            UnifierApi.Logger.Info(
                GetString("No active console task is selected."),
                category: "Console");
            return;
        }

        var task = GetParticularString("{0} is task category, {1} is task message",
            $"[{activity.Value.Category}] {activity.Value.Message}");
        switch (result) {
            case ActivityCancelRequestResult.Requested:
                UnifierApi.Logger.Info(
                    GetParticularString("{0} is selected task description", $"Interrupt requested for task {task}."),
                    category: "Console");
                break;

            case ActivityCancelRequestResult.AlreadyRequested:
                UnifierApi.Logger.Info(
                    GetParticularString("{0} is selected task description", $"Interrupt is already pending for task {task}."),
                    category: "Console");
                break;

            default:
                UnifierApi.Logger.Info(
                    GetParticularString("{0} is selected task description", $"Selected task is no longer active: {task}."),
                    category: "Console");
                break;
        }
    }

    private static string ResolvePromptLabel(ProjectionDocumentContent content) {
        var label = PromptProjectionDocumentFactory.FindState<TextProjectionNodeState>(
            content,
            PromptProjectionDocumentFactory.NodeIds.Label,
            EditorProjectionSemanticKeys.InputLabel);
        var prompt = PromptProjectionDocumentFactory.ReadText(label?.State.Content);
        return string.IsNullOrWhiteSpace(prompt) ? "> " : prompt;
    }

    private void HandleStatusPublication(long sequence, ProjectionDocument document) {
        if (Volatile.Read(ref disposed) != 0) {
            return;
        }

        if (sequence <= Interlocked.Read(ref latestStatusSequence)) {
            return;
        }

        Interlocked.Exchange(ref latestStatusSequence, sequence);
        shell.UpdateStatusDocument(
            document,
            sequence);
    }

    private static string ReadPlainLine(string? prompt, bool trim) {
        if (!string.IsNullOrEmpty(prompt)) {
            LauncherSurfaceConsole.WriteOriginal(prompt);
        }

        var line = LauncherSurfaceConsole.ReadOriginalLine() ?? string.Empty;
        return trim ? line.Trim() : line;
    }
}
