using UnifierTSL.Contracts.Sessions;
using UnifierTSL.TextEditing;

namespace Atelier.Session
{
    internal sealed record SessionRevision(long ClientBufferRevision, long CommittedRevision, string DraftText, int CaretIndex, ClientBufferedTextMarker[] Markers)
    {
        public static SessionRevision Empty { get; } = new(0, 0, string.Empty, 0, []);

        public SessionRevision WithDraft(long clientBufferRevision, string draftText, int caretIndex, IReadOnlyList<ClientBufferedTextMarker>? markers = null) {
            var normalizedDraft = draftText ?? string.Empty;
            return new SessionRevision(
                Math.Max(0, clientBufferRevision),
                CommittedRevision,
                normalizedDraft,
                Math.Clamp(caretIndex, 0, normalizedDraft.Length),
                EditorTextMarkerOps.Normalize(markers ?? Markers, normalizedDraft.Length));
        }

        public SessionRevision AdvanceCommittedAndClearDraft() {
            return new SessionRevision(checked(ClientBufferRevision + 1), CommittedRevision + 1, string.Empty, 0, []);
        }

        public SessionRevision ResetSession() {
            return new SessionRevision(checked(ClientBufferRevision + 1), 0, string.Empty, 0, []);
        }

        public SyntheticDocument BuildSyntheticDocument() {
            var decodedDraft = DraftMarkers.Decode(DraftText, Markers, CaretIndex);
            return new SyntheticDocument(
                decodedDraft.SourceText,
                decodedDraft.SourceText,
                0,
                decodedDraft.SourceText.Length,
                decodedDraft.SourceCaretIndex,
                decodedDraft.SourceMarkers);
        }
    }
}
