using UnifierTSL.Contracts.Sessions;
using UnifierTSL.Contracts.Terminal;
using UnifierTSL.TextEditing;

namespace Atelier.Presentation.Window.Drafting
{
    internal sealed record EditorBufferSnapshot(
        long ClientBufferRevision,
        long AcceptedRemoteRevision,
        EditorPaneKind Kind,
        string Text,
        int CaretIndex,
        ClientBufferedTextMarker[] Markers,
        ClientBufferedEditorSelection[] Selections,
        ClientBufferedEditorCollection[] Collections)
    {
        public static EditorBufferSnapshot Empty { get; } = new(
            0,
            0,
            EditorPaneKind.MultiLine,
            string.Empty,
            0,
            [],
            [],
            []);

        public static EditorBufferSnapshot From(ClientBufferedEditorState state) {
            var text = state.BufferText ?? string.Empty;
            return new EditorBufferSnapshot(
                Math.Max(0, state.ClientBufferRevision),
                Math.Max(0, state.AcceptedRemoteRevision),
                state.Kind,
                text,
                Math.Clamp(state.CaretIndex, 0, text.Length),
                EditorTextMarkerOps.Normalize(state.Markers, text.Length),
                [.. (state.Selections ?? [])],
                [.. (state.Collections ?? [])]);
        }

        public ClientBufferedEditorState ToClientState() {
            return new ClientBufferedEditorState {
                Kind = Kind,
                BufferText = Text,
                CaretIndex = Math.Clamp(CaretIndex, 0, Text.Length),
                ClientBufferRevision = ClientBufferRevision,
                AcceptedRemoteRevision = AcceptedRemoteRevision,
                Markers = [.. Markers],
                Selections = [.. Selections],
                Collections = [.. Collections],
            };
        }

        public bool HasSameVisibleBuffer(ClientBufferedEditorState state) {
            var text = state.BufferText ?? string.Empty;
            return Kind == state.Kind
                && string.Equals(Text, text, StringComparison.Ordinal)
                && CaretIndex == Math.Clamp(state.CaretIndex, 0, text.Length)
                && EditorTextMarkerOps.ContentEquals(Markers, state.Markers);
        }
    }
}
