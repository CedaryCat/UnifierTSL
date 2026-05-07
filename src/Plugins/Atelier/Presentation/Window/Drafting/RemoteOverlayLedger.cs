using Atelier.Session;
using UnifierTSL.Surface.Prompting.Model;
using UnifierTSL.Contracts.Sessions;
using UnifierTSL.TextEditing;

namespace Atelier.Presentation.Window.Drafting
{
    internal sealed class RemoteOverlayLedger
    {
        // Atelier remote proposals are replacement projections over the latest client base, not a
        // composable edit stack. Entries keep revision history pruneable by AcceptedRemoteRevision;
        // Latest is the only projection that participates in lightweight rebase and publication.
        private readonly List<RemoteOverlayProposal> entries = [];
        private RemoteOverlayProposal? current;

        public long HeadRevision { get; private set; }
        public IReadOnlyList<RemoteOverlayProposal> Entries => entries;
        public RemoteOverlayProposal? Latest => current;

        public void ConfirmThrough(long acceptedRemoteRevision) {
            var accepted = Math.Max(0, acceptedRemoteRevision);
            if (accepted > HeadRevision) {
                HeadRevision = accepted;
            }

            entries.RemoveAll(entry => entry.RemoteRevision <= accepted);
            if (current is not null && current.RemoteRevision <= accepted) {
                current = null;
            }
        }

        public void ClearCurrentProjection() {
            current = null;
        }

        public RemoteOverlayProposal? UpsertProjection(
            EditorBufferSnapshot baseSnapshot,
            ClientBufferedEditorState projectedState,
            DraftSnapshot projectedDraft,
            IReadOnlyList<PromptHighlightSpan>? projectedSourceHighlights) {
            if (baseSnapshot.HasSameVisibleBuffer(projectedState)) {
                current = null;
                return null;
            }

            var projected = NormalizeProjectedState(baseSnapshot, projectedState);
            if (current is { } latest && HasSameVisibleBuffer(latest.ProjectedState, projected)) {
                latest.Update(baseSnapshot, projected, projectedDraft, projectedSourceHighlights);
                return latest;
            }

            var proposal = new RemoteOverlayProposal(checked(++HeadRevision), baseSnapshot, projected, projectedDraft, projectedSourceHighlights);
            entries.Add(proposal);
            current = proposal;
            return proposal;
        }

        public RemoteOverlayProposal AppendProjection(
            EditorBufferSnapshot baseSnapshot,
            ClientBufferedEditorState projectedState,
            DraftSnapshot projectedDraft,
            IReadOnlyList<PromptHighlightSpan>? projectedSourceHighlights) {
            var proposal = new RemoteOverlayProposal(
                checked(++HeadRevision),
                baseSnapshot,
                NormalizeProjectedState(baseSnapshot, projectedState),
                projectedDraft,
                projectedSourceHighlights);
            entries.Add(proposal);
            current = proposal;
            return proposal;
        }

        private static ClientBufferedEditorState NormalizeProjectedState(
            EditorBufferSnapshot baseSnapshot,
            ClientBufferedEditorState projectedState) {
            var text = projectedState.BufferText ?? string.Empty;
            return new ClientBufferedEditorState {
                Kind = projectedState.Kind,
                BufferText = text,
                CaretIndex = Math.Clamp(projectedState.CaretIndex, 0, text.Length),
                ClientBufferRevision = baseSnapshot.ClientBufferRevision,
                AcceptedRemoteRevision = baseSnapshot.AcceptedRemoteRevision,
                Markers = EditorTextMarkerOps.Normalize(projectedState.Markers, text.Length),
                Selections = [.. (projectedState.Selections ?? [])],
                Collections = [.. (projectedState.Collections ?? [])],
            };
        }

        private static bool HasSameVisibleBuffer(ClientBufferedEditorState left, ClientBufferedEditorState right) {
            var leftText = left.BufferText ?? string.Empty;
            var rightText = right.BufferText ?? string.Empty;
            return left.Kind == right.Kind
                && string.Equals(leftText, rightText, StringComparison.Ordinal)
                && Math.Clamp(left.CaretIndex, 0, leftText.Length) == Math.Clamp(right.CaretIndex, 0, rightText.Length)
                && EditorTextMarkerOps.ContentEquals(left.Markers, right.Markers);
        }
    }

    internal sealed class RemoteOverlayProposal(
        long remoteRevision,
        EditorBufferSnapshot baseSnapshot,
        ClientBufferedEditorState projectedState,
        DraftSnapshot projectedDraft,
        IReadOnlyList<PromptHighlightSpan>? projectedSourceHighlights)
    {
        public long RemoteRevision { get; } = remoteRevision;
        public EditorBufferSnapshot BaseSnapshot { get; private set; } = baseSnapshot;
        public ClientBufferedEditorState ProjectedState { get; private set; } = projectedState;
        public DraftSnapshot ProjectedDraft { get; private set; } = projectedDraft;
        public PromptHighlightSpan[] ProjectedSourceHighlights { get; private set; } = [.. (projectedSourceHighlights ?? [])];

        public void Update(
            EditorBufferSnapshot baseSnapshot,
            ClientBufferedEditorState projectedState,
            DraftSnapshot projectedDraft,
            IReadOnlyList<PromptHighlightSpan>? projectedSourceHighlights) {
            BaseSnapshot = baseSnapshot;
            ProjectedState = projectedState;
            ProjectedDraft = projectedDraft;
            ProjectedSourceHighlights = [.. (projectedSourceHighlights ?? [])];
        }

        public void UpdateSourceHighlights(IReadOnlyList<PromptHighlightSpan>? projectedSourceHighlights) {
            ProjectedSourceHighlights = [.. (projectedSourceHighlights ?? [])];
        }
    }
}
