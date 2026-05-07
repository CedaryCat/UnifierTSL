using UnifierTSL.Surface.Activities;
using System.Text;
using UnifierTSL.Surface;
using UnifierTSL.Surface.Prompting;
using UnifierTSL.Surface.Status;
using UnifierTSL.Contracts.Projection;
using UnifierTSL.Contracts.Protocol.Payloads;
using UnifierTSL.Contracts.Sessions;
using UnifierTSL.Terminal;

namespace UnifierTSL.Surface.Hosting.Server;

public abstract partial class ServerSurfaceConsole
{
    private ServerConsoleReadScope? consoleReadScope;
    private StatusProjectionRuntime? statusRuntime;
    private bool runtimeInitialized;
    private ConsoleColor cachedBackgroundColor = System.Console.BackgroundColor;
    private ConsoleColor cachedForegroundColor = System.Console.ForegroundColor;
    private Encoding cachedInputEncoding = System.Console.InputEncoding;
    private Encoding cachedOutputEncoding = System.Console.OutputEncoding;
    private int cachedWindowHeight = System.Console.WindowHeight;
    private int cachedWindowLeft = System.Console.WindowLeft;
    private int cachedWindowTop = System.Console.WindowTop;
    private int cachedWindowWidth = System.Console.WindowWidth;
    private string cachedTitle = string.Empty;

    public virtual bool HasActiveSurfaceActivity => StatusRuntime.HasActiveActivity;

    public virtual ActivityHandle BeginSurfaceActivity(
        string category,
        string message,
        ActivityDisplayOptions display = default,
        CancellationToken cancellationToken = default) {
        return StatusRuntime.BeginActivity(category, message, display, cancellationToken);
    }

    public virtual bool TryCancelCurrentSurfaceActivity() {
        return StatusRuntime.TryCancelCurrentActivity();
    }

    public override ConsoleColor BackgroundColor {
        get => cachedBackgroundColor;
        set => cachedBackgroundColor = value;
    }

    public override ConsoleColor ForegroundColor {
        get => cachedForegroundColor;
        set => cachedForegroundColor = value;
    }

    public override Encoding InputEncoding {
        get => cachedInputEncoding;
        set => UpdateSurfaceHostOperation(ref cachedInputEncoding, value, static encoding => SurfaceHostOperations.SetInputEncoding(encoding.WebName));
    }

    public override Encoding OutputEncoding {
        get => cachedOutputEncoding;
        set => UpdateSurfaceHostOperation(ref cachedOutputEncoding, value, static encoding => SurfaceHostOperations.SetOutputEncoding(encoding.WebName));
    }

    public override int WindowWidth {
        get => cachedWindowWidth;
        set => UpdateSurfaceHostOperation(ref cachedWindowWidth, value, static width => SurfaceHostOperations.SetSize(width: width));
    }

    public override int WindowHeight {
        get => cachedWindowHeight;
        set => UpdateSurfaceHostOperation(ref cachedWindowHeight, value, static height => SurfaceHostOperations.SetSize(height: height));
    }

    public override int WindowLeft {
        get => cachedWindowLeft;
        set => UpdateSurfaceHostOperation(ref cachedWindowLeft, value, static left => SurfaceHostOperations.SetPosition(left: left));
    }

    public override int WindowTop {
        get => cachedWindowTop;
        set => UpdateSurfaceHostOperation(ref cachedWindowTop, value, static top => SurfaceHostOperations.SetPosition(top: top));
    }

    public override string Title {
        get => cachedTitle;
        set => UpdateSurfaceHostOperation(ref cachedTitle, value, static title => SurfaceHostOperations.SetTitle(title));
    }

    public void WriteAnsi(string? value) {
        PublishAnsi(StreamPayloadKind.AppendText, value);
    }

    public void WriteLineAnsi(string? value) {
        PublishAnsi(StreamPayloadKind.AppendLine, value);
    }

    public override void Write(string? value) {
        PublishConsoleText(StreamPayloadKind.AppendText, value);
    }

    public override void WriteLine(string? value) {
        PublishConsoleText(StreamPayloadKind.AppendLine, value);
    }

    public override void Clear() {
        Session.PublishSurfaceHostOperation(SurfaceHostOperations.Clear());
    }

    public override string? ReadLine() {
        return ConsoleReadScope.ReadLine();
    }

    public virtual string ReadLine(PromptSurfaceSpec prompt, bool trim = false) {
        ArgumentNullException.ThrowIfNull(prompt);
        var line = ReadLineCore(prompt) ?? string.Empty;
        return trim ? line.Trim() : line;
    }

    public override ConsoleKeyInfo ReadKey() {
        return ConsoleReadScope.ReadKey(intercept: false);
    }

    public override ConsoleKeyInfo ReadKey(bool intercept) {
        return ConsoleReadScope.ReadKey(intercept);
    }

    public override int Read() {
        return ConsoleReadScope.Read();
    }

    public override void Dispose(bool disposing) {
        if (disposing && runtimeInitialized) {
            try {
                StatusRuntime.Dispose();
            }
            catch {
            }

            SurfaceRuntimeOptions.StatusAppearanceChanged -= HandleConsoleAppearanceChanged;
            Session.PresentationAttached -= HandlePresentationAttached;
            Session.ActivitySelectionRequested -= HandleActivitySelectionRequested;
            Session.InputReceived -= HandleInputPayload;
            ConsoleReadScope.Dispose();
            Session.Dispose();
        }
        base.Dispose(disposing);
    }

    protected virtual string? ReadLineCore(PromptSurfaceSpec prompt) {
        ArgumentNullException.ThrowIfNull(prompt);
        return ConsoleReadScope.ReadLine(prompt);
    }

    private ServerConsoleReadScope ConsoleReadScope => consoleReadScope
        ?? throw new InvalidOperationException(GetString($"{GetType().FullName} was not initialized."));

    private StatusProjectionRuntime StatusRuntime => statusRuntime
        ?? throw new InvalidOperationException(GetString($"{GetType().FullName} was not initialized."));

    private void InitializeRuntime() {
        if (runtimeInitialized) {
            throw new InvalidOperationException(GetString($"{GetType().FullName} was already initialized."));
        }

        Session.ActivitySelectionRequested += HandleActivitySelectionRequested;
        Session.InputReceived += HandleInputPayload;
        Session.PresentationAttached += HandlePresentationAttached;
        SurfaceRuntimeOptions.StatusAppearanceChanged += HandleConsoleAppearanceChanged;
        consoleReadScope = new ServerConsoleReadScope(
            Session,
            CreateDefaultPromptSpec,
            Write,
            WriteLine);
        statusRuntime = new StatusProjectionRuntime(
            Server,
            PublishStatusDocument,
            shouldPublish: () => Session.IsPresentationAttached);
        Session.Start();
        runtimeInitialized = true;
    }

    private void HandleActivitySelectionRequested(int delta) {
        StatusRuntime.TrySelectRelativeActivity(delta);
    }

    private void HandleInputPayload(InputEventPayload payload) {
        if (payload.Event.Command != InteractionCommandIds.ActivityCancelSelected) {
            return;
        }

        var result = StatusRuntime.TryCancelSelectedActivity(out var activity);
        if (!activity.HasValue) {
            WriteLine(GetString("No active console task is selected."));
            return;
        }

        var task = GetParticularString("{0} is task category, {1} is task message",
            $"[{activity.Value.Category}] {activity.Value.Message}");
        switch (result) {
            case ActivityCancelRequestResult.Requested:
                WriteLine(GetParticularString("{0} is selected task description", $"Interrupt requested for task {task}."));
                break;

            case ActivityCancelRequestResult.AlreadyRequested:
                WriteLine(GetParticularString("{0} is selected task description", $"Interrupt is already pending for task {task}."));
                break;

            default:
                WriteLine(GetParticularString("{0} is selected task description", $"Selected task is no longer active: {task}."));
                break;
        }
    }

    private void HandleConsoleAppearanceChanged() {
        if (!Session.IsPresentationAttached) {
            return;
        }

        StatusRuntime.ResetChangeTracking();
    }

    private void HandlePresentationAttached() {
        StatusRuntime.RepublishCurrent();
    }

    private void PublishStatusDocument(long _, ProjectionDocument document) {
        if (!Session.IsPresentationAttached) {
            return;
        }

        Session.PublishProjectionSnapshot(new ProjectionSnapshotPayload {
            Body = new ProjectionFullSnapshotBody {
                Document = document,
            },
        });
    }

    private void UpdateSurfaceHostOperation<T>(
        ref T cachedValue,
        T value,
        Func<T, SurfaceHostOperation> createOperation) {
        cachedValue = value;
        Session.PublishSurfaceHostOperation(createOperation(value));
    }

    private void PublishAnsi(StreamPayloadKind kind, string? value) {
        PublishTextOutput(kind, value, wrapCurrentColors: false);
    }

    private void PublishConsoleText(StreamPayloadKind kind, string? value) {
        PublishTextOutput(kind, value, wrapCurrentColors: true);
    }

    private void PublishTextOutput(StreamPayloadKind kind, string? value, bool wrapCurrentColors) {
        if (string.IsNullOrEmpty(value)) {
            if (kind == StreamPayloadKind.AppendLine) {
                Session.PublishSurfaceOperation(SurfaceOperations.Stream(
                    kind,
                    text: string.Empty,
                    isAnsi: true));
            }

            return;
        }

        Session.PublishSurfaceOperation(SurfaceOperations.Stream(
            kind,
            text: wrapCurrentColors ? WrapCurrentColors(value) : value,
            isAnsi: true));
    }

    private string WrapCurrentColors(string value) {
        var sanitized = AnsiSanitizer.SanitizeEscapes(value);
        return AnsiColorCodec.Wrap(sanitized, cachedForegroundColor, cachedBackgroundColor);
    }
}
