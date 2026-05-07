using Atelier.Session;

namespace Atelier.Presentation.Window.Drafting {
    internal static class VisibleSourceMapper {
        public static bool TryMapVisibleEdit(
            DraftSnapshot previousDraft,
            VisibleEdit visibleEdit,
            out SourceEdit sourceEdit) {
            sourceEdit = default;
            return visibleEdit.Kind switch {
                VisibleEditKind.Insert => TryMapPlainInsert(previousDraft, visibleEdit, out sourceEdit),
                VisibleEditKind.Delete => TryMapPlainDelete(previousDraft, visibleEdit, out sourceEdit),
                VisibleEditKind.Replace => TryMapPlainReplace(previousDraft, visibleEdit, out sourceEdit),
                VisibleEditKind.MarkerDelete => TryMapMarkerEdit(previousDraft, visibleEdit, SourceEditKind.VirtualMarkerDelete, out sourceEdit),
                VisibleEditKind.MarkerMaterialize => TryMapMarkerEdit(previousDraft, visibleEdit, SourceEditKind.VirtualMarkerMaterialize, out sourceEdit),
                _ => false,
            };
        }

        public static bool TryMapEncodedPosition(
            DraftSnapshot draft,
            int encodedPosition,
            bool preferEnd,
            out int sourcePosition) {
            var encoded = Math.Max(0, encodedPosition);
            var encodedIndex = 0;
            var sourceIndex = 0;
            foreach (var marker in draft.SourceMarkers.OrderBy(static marker => marker.EncodedStartIndex)) {
                if (encoded < marker.EncodedStartIndex) {
                    sourcePosition = sourceIndex + encoded - encodedIndex;
                    return sourcePosition >= 0 && sourcePosition <= draft.SourceText.Length;
                }

                var markerEncodedEnd = marker.EncodedStartIndex + marker.EncodedLength;
                if (encoded == marker.EncodedStartIndex) {
                    sourcePosition = preferEnd
                        ? marker.SourceStartIndex + marker.SourceLength
                        : marker.SourceStartIndex;
                    return true;
                }

                if (encoded > marker.EncodedStartIndex && encoded < markerEncodedEnd) {
                    sourcePosition = -1;
                    return false;
                }

                if (encoded == markerEncodedEnd) {
                    sourcePosition = marker.SourceStartIndex + marker.SourceLength;
                    return true;
                }

                encodedIndex = markerEncodedEnd;
                sourceIndex = marker.SourceStartIndex + marker.SourceLength;
            }

            sourcePosition = sourceIndex + encoded - encodedIndex;
            return sourcePosition >= 0 && sourcePosition <= draft.SourceText.Length;
        }

        private static bool TryMapPlainInsert(
            DraftSnapshot previousDraft,
            VisibleEdit visibleEdit,
            out SourceEdit sourceEdit) {
            sourceEdit = default;
            if (visibleEdit.EncodedRemovedLength != 0
                || !TryMapEncodedPosition(previousDraft, visibleEdit.EncodedStartIndex, preferEnd: false, out var sourceStart)
                || EncodedPositionTouchesMarkerInterior(previousDraft, visibleEdit.EncodedStartIndex)) {
                return false;
            }

            sourceEdit = new SourceEdit(sourceStart, 0, visibleEdit.InsertedText);
            return true;
        }

        private static bool TryMapPlainDelete(
            DraftSnapshot previousDraft,
            VisibleEdit visibleEdit,
            out SourceEdit sourceEdit) {
            sourceEdit = default;
            var encodedEnd = visibleEdit.EncodedStartIndex + visibleEdit.EncodedRemovedLength;
            if (visibleEdit.EncodedRemovedLength <= 0
                || EncodedRangeOverlapsMarker(previousDraft, visibleEdit.EncodedStartIndex, encodedEnd)
                || !TryMapEncodedPosition(previousDraft, visibleEdit.EncodedStartIndex, preferEnd: false, out var sourceStart)
                || !TryMapEncodedPosition(previousDraft, encodedEnd, preferEnd: false, out var sourceEnd)
                || sourceEnd < sourceStart) {
                return false;
            }

            sourceEdit = new SourceEdit(sourceStart, sourceEnd - sourceStart, string.Empty);
            return true;
        }

        private static bool TryMapPlainReplace(
            DraftSnapshot previousDraft,
            VisibleEdit visibleEdit,
            out SourceEdit sourceEdit) {
            sourceEdit = default;
            var encodedEnd = visibleEdit.EncodedStartIndex + visibleEdit.EncodedRemovedLength;
            if (visibleEdit.EncodedRemovedLength <= 0
                || string.IsNullOrEmpty(visibleEdit.InsertedText)
                || EncodedRangeOverlapsMarker(previousDraft, visibleEdit.EncodedStartIndex, encodedEnd)
                || !TryMapEncodedPosition(previousDraft, visibleEdit.EncodedStartIndex, preferEnd: false, out var sourceStart)
                || !TryMapEncodedPosition(previousDraft, encodedEnd, preferEnd: false, out var sourceEnd)
                || sourceEnd < sourceStart) {
                return false;
            }

            sourceEdit = new SourceEdit(sourceStart, sourceEnd - sourceStart, visibleEdit.InsertedText);
            return true;
        }

        private static bool TryMapMarkerEdit(
            DraftSnapshot previousDraft,
            VisibleEdit visibleEdit,
            SourceEditKind kind,
            out SourceEdit sourceEdit) {
            sourceEdit = default;
            var marker = previousDraft.SourceMarkers.FirstOrDefault(marker =>
                marker.EncodedStartIndex == visibleEdit.EncodedStartIndex
                && marker.EncodedLength == visibleEdit.EncodedRemovedLength);
            if (marker is null || marker.PairId <= 0) {
                return false;
            }

            sourceEdit = new SourceEdit(
                marker.SourceStartIndex,
                marker.SourceLength,
                kind == SourceEditKind.VirtualMarkerMaterialize ? visibleEdit.InsertedText : string.Empty,
                kind,
                marker.PairId);
            return true;
        }

        private static bool EncodedPositionTouchesMarkerInterior(DraftSnapshot draft, int encodedPosition) {
            return draft.SourceMarkers.Any(marker =>
                encodedPosition > marker.EncodedStartIndex
                && encodedPosition < marker.EncodedStartIndex + marker.EncodedLength);
        }

        private static bool EncodedRangeOverlapsMarker(DraftSnapshot draft, int encodedStart, int encodedEnd) {
            return draft.SourceMarkers.Any(marker =>
                encodedStart < marker.EncodedStartIndex + marker.EncodedLength
                && encodedEnd > marker.EncodedStartIndex);
        }
    }
}
