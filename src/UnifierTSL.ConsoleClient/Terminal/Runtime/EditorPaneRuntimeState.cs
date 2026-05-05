using UnifierTSL.Contracts.Terminal;
using UnifierTSL.Contracts.Projection;
using UnifierTSL.Contracts.Sessions;

namespace UnifierTSL.Terminal.Runtime
{
    internal sealed class EditorPaneRuntimeState
    {
        public EditorPaneKind Kind { get; init; } = EditorPaneKind.SingleLine;
        public EditorAuthority Authority { get; init; } = EditorAuthority.ClientBuffered;
        public string BufferText { get; init; } = string.Empty;
        public int CaretIndex { get; init; }
        public long ExpectedClientBufferRevision { get; init; }
        public long RemoteRevision { get; init; }
        public ClientBufferedTextMarker[] Markers { get; init; } = [];
        public ProjectionMarkerCatalogItem[] MarkerCatalog { get; init; } = [];
        public bool AcceptsSubmit { get; init; } = true;
        public EditorKeymap Keymap { get; init; } = new();
        public EditorAuthoringBehavior AuthoringBehavior { get; init; } = new();

        public bool HasSameInteractionMode(EditorPaneRuntimeState other) {
            return Kind == other.Kind
                && Authority == other.Authority
                && AcceptsSubmit == other.AcceptsSubmit;
        }
    }
}
