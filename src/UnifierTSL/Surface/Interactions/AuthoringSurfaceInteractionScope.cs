using UnifierTSL.Contracts.Protocol.Payloads;
using UnifierTSL.Contracts.Sessions;
using UnifierTSL.Surface.Hosting;

namespace UnifierTSL.Surface.Interactions;

public sealed class AuthoringSurfaceInteractionScope : SurfaceInteractionScope
{
    public AuthoringSurfaceInteractionScope(ISurfaceSession session, SurfaceInteractionScopeOptions options)
        : base(session, options) {
        session.InputReceived += OnInputReceived;
    }

    public event Action<ClientBufferedEditorState>? EditorStateSyncReceived;
    public event Action<ClientBufferedEditorState>? SubmitReceived;
    public event Action<ConsoleKeyInfo>? KeyReceived;

    private void OnInputReceived(InputEventPayload payload) {
        if (payload.InteractionScopeId != Scope.Id) {
            return;
        }

        if (payload.Event.Kind == InputEventKind.Key && payload.Event.KeyInfo is { } keyInfo) {
            if (CanAcceptSessionEvent()) {
                KeyReceived?.Invoke(keyInfo.ToConsoleKeyInfo());
            }

            return;
        }

        if (payload.BufferedEditorState is not { } bufferedState) {
            return;
        }

        if (!CanAcceptSessionEvent()) {
            return;
        }

        bool isEditorStateSync = payload.Event.Kind == InputEventKind.EditorStateSync;
        bool isSubmit = payload.Event.Kind == InputEventKind.Submit;
        if (!isEditorStateSync && !isSubmit) {
            return;
        }

        if (isEditorStateSync) {
            EditorStateSyncReceived?.Invoke(bufferedState);
        }

        if (isSubmit) {
            SubmitReceived?.Invoke(bufferedState);
        }
    }

    protected override void DetachSessionHandlers() {
        Session.InputReceived -= OnInputReceived;
        base.DetachSessionHandlers();
    }
}
