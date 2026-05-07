using UnifierTSL.Contracts.Sessions;

namespace UnifierTSL.TextEditing
{
    public static class EditorTextMarkerOps
    {
        public static ClientBufferedTextMarker[] Normalize(IReadOnlyList<ClientBufferedTextMarker>? markers, int textLength)
        {
            List<ClientBufferedTextMarker> normalized = [];
            var boundedTextLength = Math.Max(0, textLength);
            foreach (var marker in markers ?? [])
            {
                if (marker is null || string.IsNullOrWhiteSpace(marker.Key))
                {
                    continue;
                }

                var start = Math.Clamp(marker.StartIndex, 0, boundedTextLength);
                var length = Math.Clamp(marker.Length, 0, boundedTextLength - start);
                if (length <= 0)
                {
                    continue;
                }

                normalized.Add(new ClientBufferedTextMarker
                {
                    Key = marker.Key,
                    VariantKey = marker.VariantKey ?? string.Empty,
                    StartIndex = start,
                    Length = length,
                });
            }

            normalized.Sort(static (left, right) => left.StartIndex != right.StartIndex
                ? left.StartIndex.CompareTo(right.StartIndex)
                : left.Length.CompareTo(right.Length));

            List<ClientBufferedTextMarker> filtered = [];
            var nextAllowedStart = 0;
            foreach (var marker in normalized)
            {
                if (marker.StartIndex < nextAllowedStart)
                {
                    continue;
                }

                filtered.Add(marker);
                nextAllowedStart = marker.StartIndex + marker.Length;
            }

            return [.. filtered];
        }

        public static bool ContentEquals(IReadOnlyList<ClientBufferedTextMarker>? left, IReadOnlyList<ClientBufferedTextMarker>? right)
        {
            var leftItems = left ?? [];
            var rightItems = right ?? [];
            if (leftItems.Count != rightItems.Count)
            {
                return false;
            }

            for (var index = 0; index < leftItems.Count; index++)
            {
                var leftMarker = leftItems[index];
                var rightMarker = rightItems[index];
                if (!string.Equals(leftMarker.Key, rightMarker.Key, StringComparison.Ordinal)
                    || !string.Equals(leftMarker.VariantKey ?? string.Empty, rightMarker.VariantKey ?? string.Empty, StringComparison.Ordinal)
                    || leftMarker.StartIndex != rightMarker.StartIndex
                    || leftMarker.Length != rightMarker.Length)
                {
                    return false;
                }
            }

            return true;
        }

        public static int SnapCaretToBoundary(IReadOnlyList<ClientBufferedTextMarker>? markers, int caretIndex, int textLength)
        {
            var boundedCaret = Math.Clamp(caretIndex, 0, Math.Max(0, textLength));
            return TryFindContainingMarker(markers, boundedCaret, out var marker)
                ? marker.StartIndex
                : boundedCaret;
        }

        public static bool TryFindMarkerAtCursor(IReadOnlyList<ClientBufferedTextMarker>? markers, int cursorIndex, out ClientBufferedTextMarker marker)
        {
            foreach (var candidate in markers ?? [])
            {
                if (candidate is not null && candidate.StartIndex == cursorIndex)
                {
                    marker = candidate;
                    return true;
                }
            }

            marker = null!;
            return false;
        }

        public static bool TryFindMarkerBeforeCursor(IReadOnlyList<ClientBufferedTextMarker>? markers, int cursorIndex, out ClientBufferedTextMarker marker)
        {
            foreach (var candidate in markers ?? [])
            {
                if (candidate is not null && candidate.StartIndex + candidate.Length == cursorIndex)
                {
                    marker = candidate;
                    return true;
                }
            }

            marker = null!;
            return false;
        }

        public static bool TryFindContainingMarker(IReadOnlyList<ClientBufferedTextMarker>? markers, int cursorIndex, out ClientBufferedTextMarker marker)
        {
            foreach (var candidate in markers ?? [])
            {
                if (candidate is null)
                {
                    continue;
                }

                var start = candidate.StartIndex;
                var end = candidate.StartIndex + candidate.Length;
                if (cursorIndex > start && cursorIndex < end)
                {
                    marker = candidate;
                    return true;
                }
            }

            marker = null!;
            return false;
        }

        public static ClientBufferedTextMarker[] ApplyTextChange(
            IReadOnlyList<ClientBufferedTextMarker>? markers,
            int startIndex,
            int removedLength,
            int insertedLength,
            int textLength)
        {
            var previousTextLength = Math.Max(0, textLength + Math.Max(0, removedLength) - Math.Max(0, insertedLength));
            var normalized = Normalize(markers, previousTextLength);
            var boundedStart = Math.Max(0, startIndex);
            var boundedRemovedLength = Math.Max(0, removedLength);
            var boundedInsertedLength = Math.Max(0, insertedLength);
            var delta = boundedInsertedLength - boundedRemovedLength;
            var changeEnd = boundedStart + boundedRemovedLength;
            List<ClientBufferedTextMarker> updated = [];
            foreach (var marker in normalized)
            {
                var markerEnd = marker.StartIndex + marker.Length;
                if (markerEnd <= boundedStart)
                {
                    updated.Add(marker);
                    continue;
                }

                if (marker.StartIndex >= changeEnd)
                {
                    updated.Add(new ClientBufferedTextMarker
                    {
                        Key = marker.Key,
                        VariantKey = marker.VariantKey,
                        StartIndex = marker.StartIndex + delta,
                        Length = marker.Length,
                    });
                }
            }

            return Normalize(updated, textLength);
        }
    }
}
