using Atelier.Session;
using System.Collections.Immutable;
using UnifierTSL.Contracts.Sessions;
using UnifierTSL.TextEditing;

namespace Atelier.Presentation.Window.Drafting {
    internal enum VisibleEditKind {
        NoOp,
        CaretMove,
        Insert,
        Delete,
        Replace,
        MarkerDelete,
        MarkerMaterialize,
    }

    internal readonly record struct VisibleEdit(
        VisibleEditKind Kind,
        int EncodedStartIndex,
        int EncodedRemovedLength,
        string InsertedText,
        int PreviousCaretIndex,
        int CurrentCaretIndex,
        ImmutableArray<ClientBufferedTextMarker> RemovedMarkers,
        ClientBufferedTextMarker? ReplacedMarker);

    internal static class VisibleEditSolver {
        public static bool TrySolve(
            ClientBufferedEditorState previousState,
            ClientBufferedEditorState currentState,
            out VisibleEdit edit) {

            var previousText = previousState.BufferText ?? string.Empty;
            var currentText = currentState.BufferText ?? string.Empty;
            var previousCaret = Math.Clamp(previousState.CaretIndex, 0, previousText.Length);
            var currentCaret = Math.Clamp(currentState.CaretIndex, 0, currentText.Length);
            var previousMarkers = EditorTextMarkerOps.Normalize(previousState.Markers, previousText.Length);
            var currentMarkers = EditorTextMarkerOps.Normalize(currentState.Markers, currentText.Length);

            if (string.Equals(previousText, currentText, StringComparison.Ordinal)
                && EditorTextMarkerOps.ContentEquals(previousMarkers, currentMarkers)) {
                edit = new VisibleEdit(
                    previousCaret == currentCaret ? VisibleEditKind.NoOp : VisibleEditKind.CaretMove,
                    previousCaret,
                    0,
                    string.Empty,
                    previousCaret,
                    currentCaret,
                    [],
                    null);
                return true;
            }

            // Materializing a marker looks like replacing the encoded marker token with its source text,
            // so marker edits must be recognized before the generic replace fallback.
            if (TrySolveInsert(previousText, previousMarkers, previousCaret, currentText, currentMarkers, currentCaret, out edit)
                || TrySolveBackspace(previousText, previousMarkers, previousCaret, currentText, currentMarkers, currentCaret, out edit)
                || TrySolveDelete(previousText, previousMarkers, previousCaret, currentText, currentMarkers, currentCaret, out edit)
                || TrySolveMarkerDelete(previousText, previousMarkers, previousCaret, currentText, currentMarkers, currentCaret, out edit)
                || TrySolveMarkerMaterialize(previousText, previousMarkers, previousCaret, currentText, currentMarkers, currentCaret, out edit)
                || TrySolveReplace(previousText, previousMarkers, previousCaret, currentText, currentMarkers, currentCaret, out edit)) {
                return true;
            }

            edit = default;
            return false;
        }

        private static bool TrySolveInsert(
            string previousText,
            IReadOnlyList<ClientBufferedTextMarker> previousMarkers,
            int previousCaret,
            string currentText,
            IReadOnlyList<ClientBufferedTextMarker> currentMarkers,
            int currentCaret,
            out VisibleEdit edit) {
            edit = default;
            if (currentCaret <= previousCaret) {
                return false;
            }

            var insertedLength = currentCaret - previousCaret;
            if (currentText.Length != previousText.Length + insertedLength
                || previousCaret + insertedLength > currentText.Length) {
                return false;
            }

            var inserted = currentText.Substring(previousCaret, insertedLength);
            return TryCreateCandidate(
                VisibleEditKind.Insert,
                previousText,
                previousMarkers,
                currentText,
                currentMarkers,
                previousCaret,
                removedLength: 0,
                inserted,
                previousCaret,
                currentCaret,
                [],
                null,
                out edit);
        }

        private static bool TrySolveBackspace(
            string previousText,
            IReadOnlyList<ClientBufferedTextMarker> previousMarkers,
            int previousCaret,
            string currentText,
            IReadOnlyList<ClientBufferedTextMarker> currentMarkers,
            int currentCaret,
            out VisibleEdit edit) {
            edit = default;
            if (previousCaret <= currentCaret || previousText.Length <= currentText.Length) {
                return false;
            }

            var removedLength = previousCaret - currentCaret;
            if (RangeOverlapsMarker(previousMarkers, currentCaret, currentCaret + removedLength)) {
                return false;
            }

            return TryCreateCandidate(
                VisibleEditKind.Delete,
                previousText,
                previousMarkers,
                currentText,
                currentMarkers,
                currentCaret,
                removedLength,
                string.Empty,
                previousCaret,
                currentCaret,
                [],
                null,
                out edit);
        }

        private static bool TrySolveDelete(
            string previousText,
            IReadOnlyList<ClientBufferedTextMarker> previousMarkers,
            int previousCaret,
            string currentText,
            IReadOnlyList<ClientBufferedTextMarker> currentMarkers,
            int currentCaret,
            out VisibleEdit edit) {
            edit = default;
            if (previousCaret != currentCaret || previousText.Length <= currentText.Length) {
                return false;
            }

            var removedLength = previousText.Length - currentText.Length;
            if (RangeOverlapsMarker(previousMarkers, previousCaret, previousCaret + removedLength)) {
                return false;
            }

            return TryCreateCandidate(
                VisibleEditKind.Delete,
                previousText,
                previousMarkers,
                currentText,
                currentMarkers,
                previousCaret,
                removedLength,
                string.Empty,
                previousCaret,
                currentCaret,
                [],
                null,
                out edit);
        }

        private static bool TrySolveReplace(
            string previousText,
            IReadOnlyList<ClientBufferedTextMarker> previousMarkers,
            int previousCaret,
            string currentText,
            IReadOnlyList<ClientBufferedTextMarker> currentMarkers,
            int currentCaret,
            out VisibleEdit edit) {
            edit = default;
            var prefixLength = 0;
            var prefixLimit = Math.Min(previousText.Length, currentText.Length);
            while (prefixLength < prefixLimit && previousText[prefixLength] == currentText[prefixLength]) {
                prefixLength++;
            }

            var suffixLength = 0;
            while (suffixLength < previousText.Length - prefixLength
                && suffixLength < currentText.Length - prefixLength
                && previousText[previousText.Length - suffixLength - 1] == currentText[currentText.Length - suffixLength - 1]) {
                suffixLength++;
            }

            var removedLength = previousText.Length - prefixLength - suffixLength;
            var insertedLength = currentText.Length - prefixLength - suffixLength;
            if (removedLength <= 0 || insertedLength <= 0) {
                return false;
            }

            if (RangeOverlapsMarker(previousMarkers, prefixLength, prefixLength + removedLength)) {
                return false;
            }

            var inserted = currentText.Substring(prefixLength, insertedLength);
            return TryCreateCandidate(
                VisibleEditKind.Replace,
                previousText,
                previousMarkers,
                currentText,
                currentMarkers,
                prefixLength,
                removedLength,
                inserted,
                previousCaret,
                currentCaret,
                [],
                null,
                out edit);
        }

        private static bool TrySolveMarkerDelete(
            string previousText,
            IReadOnlyList<ClientBufferedTextMarker> previousMarkers,
            int previousCaret,
            string currentText,
            IReadOnlyList<ClientBufferedTextMarker> currentMarkers,
            int currentCaret,
            out VisibleEdit edit) {
            edit = default;
            foreach (var marker in previousMarkers) {
                if (previousCaret != marker.StartIndex && previousCaret != marker.StartIndex + marker.Length) {
                    continue;
                }

                if (currentCaret != marker.StartIndex) {
                    continue;
                }

                if (TryCreateCandidate(
                    VisibleEditKind.MarkerDelete,
                    previousText,
                    previousMarkers,
                    currentText,
                    currentMarkers,
                    marker.StartIndex,
                    marker.Length,
                    string.Empty,
                    previousCaret,
                    currentCaret,
                    [marker],
                    null,
                    out edit)) {
                    return true;
                }
            }

            return false;
        }

        private static bool RangeOverlapsMarker(IReadOnlyList<ClientBufferedTextMarker> markers, int start, int end) {
            return markers.Any(marker =>
                start < marker.StartIndex + marker.Length
                && end > marker.StartIndex);
        }

        private static bool TrySolveMarkerMaterialize(
            string previousText,
            IReadOnlyList<ClientBufferedTextMarker> previousMarkers,
            int previousCaret,
            string currentText,
            IReadOnlyList<ClientBufferedTextMarker> currentMarkers,
            int currentCaret,
            out VisibleEdit edit) {
            edit = default;
            foreach (var marker in previousMarkers) {
                var hasDisplayText = DraftMarkers.TryGetSourceText(marker.Key, out var displayText);
                var expectedCaret = hasDisplayText ? marker.StartIndex + displayText.Length : -1;

                if (previousCaret != marker.StartIndex
                    || !hasDisplayText
                    || string.IsNullOrEmpty(displayText)
                    || currentCaret != expectedCaret) {
                    continue;
                }

                if (TryCreateCandidate(
                    VisibleEditKind.MarkerMaterialize,
                    previousText,
                    previousMarkers,
                    currentText,
                    currentMarkers,
                    marker.StartIndex,
                    marker.Length,
                    displayText,
                    previousCaret,
                    currentCaret,
                    [marker],
                    marker,
                    out edit)) {
                    return true;
                }
            }

            return false;
        }

        private static bool TryCreateCandidate(
            VisibleEditKind kind,
            string previousText,
            IReadOnlyList<ClientBufferedTextMarker> previousMarkers,
            string currentText,
            IReadOnlyList<ClientBufferedTextMarker> currentMarkers,
            int start,
            int removedLength,
            string insertedText,
            int previousCaret,
            int currentCaret,
            ImmutableArray<ClientBufferedTextMarker> removedMarkers,
            ClientBufferedTextMarker? replacedMarker,
            out VisibleEdit edit) {
            edit = default;
            if (start < 0 || start > previousText.Length || removedLength < 0 || start + removedLength > previousText.Length) {
                return false;
            }

            var rewrittenText = previousText.Remove(start, removedLength).Insert(start, insertedText ?? string.Empty);
            if (!string.Equals(rewrittenText, currentText, StringComparison.Ordinal)) {
                return false;
            }

            var shiftedMarkers = EditorTextMarkerOps.ApplyTextChange(
                previousMarkers,
                start,
                removedLength,
                insertedText?.Length ?? 0,
                currentText.Length);
            if (!EditorTextMarkerOps.ContentEquals(shiftedMarkers, currentMarkers)) {
                return false;
            }

            edit = new VisibleEdit(
                kind,
                start,
                removedLength,
                insertedText ?? string.Empty,
                previousCaret,
                currentCaret,
                removedMarkers,
                replacedMarker);
            return true;
        }
    }
}
