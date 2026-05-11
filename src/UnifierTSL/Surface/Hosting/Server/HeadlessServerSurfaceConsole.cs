using UnifierTSL.Servers;
using UnifierTSL.Surface.Hosting;
using UnifierTSL.Contracts.Projection;
using UnifierTSL.Contracts.Protocol.Payloads;
using UnifierTSL.Contracts.Sessions;
using UnifierTSL.Surface.Prompting;

namespace UnifierTSL.Surface.Hosting.Server;

internal sealed class HeadlessServerSurfaceConsole : ServerSurfaceConsole
{
    private sealed class NullSurfaceSession : ISurfaceSession
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
        public void PublishSurfaceHostOperation(SurfaceHostOperation operation) { }
        public void PublishProjectionSnapshot(ProjectionSnapshotPayload snapshot) { }
        public void PublishSurfaceOperation(SurfaceOperation operation) { }
        public InteractionScope OpenInteractionScope(
            string interactionKind,
            bool isTransient = true) {
            return new InteractionScope {
                Id = InteractionScopeId.New(),
                State = InteractionScopeState.Active,
                Kind = interactionKind,
                IsTransient = isTransient,
            };
        }
        public void Dispose() { }
    }

    private readonly ISurfaceSession session = new NullSurfaceSession();

    public HeadlessServerSurfaceConsole(ServerContext server)
        : base(server, () => PromptSurfaceSpec.CreatePlain()) {

        InitializeSurfaceRuntime();
    }

    protected override ISurfaceSession Session => session;

    public override string? ReadLine() {
        throw CreateInputNotSupportedException();
    }

    public override ConsoleKeyInfo ReadKey() {
        throw CreateInputNotSupportedException();
    }

    public override ConsoleKeyInfo ReadKey(bool intercept) {
        throw CreateInputNotSupportedException();
    }

    public override int Read() {
        throw CreateInputNotSupportedException();
    }

    protected override string? ReadLineCore(PromptSurfaceSpec prompt) {
        throw CreateInputNotSupportedException();
    }

    private static NotSupportedException CreateInputNotSupportedException() {
        return new("Headless server surface consoles do not support interactive input.");
    }
}
