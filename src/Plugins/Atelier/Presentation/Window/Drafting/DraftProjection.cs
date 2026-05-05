using Atelier.Session;
using UnifierTSL.Contracts.Sessions;

namespace Atelier.Presentation.Window.Drafting {
    internal static class DraftProjection {
        public static (ClientBufferedEditorState State, DraftSnapshot Draft) CreateRewrittenState(
            ClientBufferedEditorState template,
            DraftSnapshot draft) {
            var encoded = DraftMarkers.Encode(draft);
            var encodedDraft = draft.With(
                sourceMarkers: DraftMarkers.CreateSourceMarkers(encoded.PairLedger, draft.SourceText.Length),
                pairLedger: encoded.PairLedger);
            var state = new ClientBufferedEditorState {
                Kind = template.Kind,
                BufferText = encoded.Text,
                CaretIndex = encoded.CaretIndex,
                ClientBufferRevision = Math.Max(0, template.ClientBufferRevision),
                AcceptedRemoteRevision = Math.Max(0, template.AcceptedRemoteRevision),
                Markers = encoded.Markers,
            };
            return (state, encodedDraft);
        }
    }
}
