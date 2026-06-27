using System.Text;
using UnifierTSL.Contracts.Display;
using UnifierTSL.Contracts.Projection;
using UnifierTSL.Contracts.Protocol.Payloads;
using UnifierTSL.Contracts.Sessions;
using UnifierTSL.Contracts.Terminal;
using UnifierTSL.Servers;
using UnifierTSL.Surface.Activities;
using UnifierTSL.Surface.Prompting;

namespace UnifierTSL.Surface.Hosting.Server;

public sealed class LauncherBackedServerSurfaceConsole : ServerSurfaceConsole
{
    private sealed class LauncherSurfaceSession : ISurfaceSession
    {
        public event Action? PresentationAttached {
            add { }
            remove { }
        }
        public event Action? PresentationDetached {
            add { }
            remove { }
        }
        public event Action? ReleaseRequested {
            add { }
            remove { }
        }
        public event Action<int>? ActivitySelectionRequested {
            add { }
            remove { }
        }
        public event Action<InputEventPayload>? InputReceived {
            add { }
            remove { }
        }
        public event Action<LifecyclePayload>? LifecycleReceived {
            add { }
            remove { }
        }
        public event Action<SurfaceCompletionPayload>? CompletionReceived {
            add { }
            remove { }
        }

        public bool IsPresentationAttached => false;

        public void Start() { }

        public void PublishSurfaceHostOperation(SurfaceHostOperation operation) {
            ArgumentNullException.ThrowIfNull(operation);
            if (SurfaceHostOperations.IsClear(operation)) {
                LauncherSurfaceConsole.WriteAnsi("\u001b[2J\u001b[H");
                return;
            }

            if (!SurfaceHostOperations.TryGetProperties(operation, out var properties)) {
                return;
            }

            if (properties.Title is { } title) {
                Console.Title = title;
            }
            if (properties.InputEncoding is { } inputEncoding) {
                Console.InputEncoding = Encoding.GetEncoding(inputEncoding);
            }
            if (properties.OutputEncoding is { } outputEncoding) {
                Console.OutputEncoding = Encoding.GetEncoding(outputEncoding);
            }
            if (OperatingSystem.IsWindows()) {
                if (properties.Width is { } width) {
                    Console.WindowWidth = width;
                }
                if (properties.Height is { } height) {
                    Console.WindowHeight = height;
                }
                if (properties.Left is { } left) {
                    Console.WindowLeft = left;
                }
                if (properties.Top is { } top) {
                    Console.WindowTop = top;
                }
            }
        }

        public void PublishProjectionSnapshot(ProjectionSnapshotPayload snapshot) { }

        public void PublishSurfaceOperation(SurfaceOperation operation) {
            ArgumentNullException.ThrowIfNull(operation);
            if (!SurfaceOperations.TryGetStream(operation, out var stream)) {
                return;
            }

            PublishStream(stream);
        }

        public InteractionScope OpenInteractionScope(string interactionKind, bool isTransient = true) {
            return new InteractionScope {
                Id = InteractionScopeId.New(),
                State = InteractionScopeState.Active,
                Kind = interactionKind,
                IsTransient = isTransient,
            };
        }

        public void Dispose() { }

        private static void PublishStream(StreamPayload stream) {
            if (stream.Channel == StreamChannel.Status) {
                return;
            }

            switch (stream.Kind) {
                case StreamPayloadKind.AppendText:
                    WriteStreamText(stream, appendLine: false);
                    break;

                case StreamPayloadKind.AppendLine:
                    WriteStreamText(stream, appendLine: true);
                    break;

                case StreamPayloadKind.Separator:
                    LauncherSurfaceConsole.WriteLine();
                    break;

                case StreamPayloadKind.Clear:
                    LauncherSurfaceConsole.WriteAnsi("\u001b[2J\u001b[H");
                    break;
            }
        }

        private static void WriteStreamText(StreamPayload stream, bool appendLine) {
            var text = stream.StyledText is { } styledText
                ? StyledTextLineOps.ToPlainText(styledText)
                : stream.Text;
            if (appendLine) {
                text += Environment.NewLine;
            }

            if (stream.IsAnsi || stream.StyledText is not null) {
                LauncherSurfaceConsole.WriteAnsi(text);
                return;
            }

            LauncherSurfaceConsole.Write(text);
        }
    }

    private readonly ISurfaceSession session = new LauncherSurfaceSession();

    public LauncherBackedServerSurfaceConsole(ServerContext server)
        : base(server, () => PromptRegistry.CreateDefaultCommandPromptSpec(server)) {
    }

    public override bool HasActiveSurfaceActivity => LauncherSurfaceConsole.HasActiveSurfaceActivity();

    protected override ISurfaceSession Session => session;

    public override ActivityHandle BeginSurfaceActivity(
        string category,
        string message,
        ActivityDisplayOptions display = default,
        CancellationToken cancellationToken = default) {
        var scope = LauncherSurfaceConsole.BeginSurfaceActivityScope(category, message, display, cancellationToken);
        if (scope.Activity is { } activity) {
            return activity;
        }

        scope.Dispose();
        return ActivityHandle.CreateNoop(category, message, display, cancellationToken);
    }

    public override bool TryCancelCurrentSurfaceActivity() {
        return false;
    }

    public override string? ReadLine() {
        return ReadLine(CreateDefaultPromptSpec(), trim: false);
    }

    public override string ReadLine(PromptSurfaceSpec prompt, bool trim = false) {
        ArgumentNullException.ThrowIfNull(prompt);
        return LauncherSurfaceConsole.ReadLine(prompt, trim);
    }

    public override ConsoleKeyInfo ReadKey() {
        return LauncherSurfaceConsole.ReadKey();
    }

    public override ConsoleKeyInfo ReadKey(bool intercept) {
        return LauncherSurfaceConsole.ReadKey(intercept);
    }

    public override int Read() {
        return Console.In.Read();
    }
}
