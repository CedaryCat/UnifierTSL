using MemoryPack;
using UnifierTSL.Contracts.Terminal;

namespace UnifierTSL.Contracts.Sessions {
    public enum SurfaceCompletionKind : byte {
        None,
        Text,
        Cancelled,
        Closed,
    }

    [MemoryPackable]
    public sealed partial class ClientBufferedEditorSelection {
        public string SemanticKey { get; init; } = string.Empty;
        public string ActiveItemId { get; init; } = string.Empty;
        public int ActiveOrdinal { get; init; }
        public string[] SelectedItemIds { get; init; } = [];
    }

    [MemoryPackable]
    public sealed partial class ClientBufferedEditorCollection {
        public string SemanticKey { get; init; } = string.Empty;
        public int TotalItemCount { get; init; }
        public int WindowOffset { get; init; }
        public int PageSize { get; init; }
    }

    [MemoryPackable]
    public sealed partial class ClientBufferedTextMarker {
        public string Key { get; init; } = string.Empty;
        public string VariantKey { get; init; } = string.Empty;
        public int StartIndex { get; init; }
        public int Length { get; init; }
    }

    [MemoryPackable]
    public sealed partial class ClientBufferedEditorState {
        public EditorPaneKind Kind { get; init; } = EditorPaneKind.SingleLine;
        public string BufferText { get; init; } = string.Empty;
        public int CaretIndex { get; init; }
        public long ClientBufferRevision { get; init; }
        public long AcceptedRemoteRevision { get; init; }
        public ClientBufferedTextMarker[] Markers { get; init; } = [];
        public ClientBufferedEditorSelection[] Selections { get; init; } = [];
        public ClientBufferedEditorCollection[] Collections { get; init; } = [];

        public ClientBufferedEditorSelection? FindSelection(string semanticKey) {
            return (Selections ?? [])
                .FirstOrDefault(selection => string.Equals(selection.SemanticKey, semanticKey, StringComparison.Ordinal));
        }

        public ClientBufferedEditorCollection? FindCollection(string semanticKey) {
            return (Collections ?? [])
                .FirstOrDefault(collection => string.Equals(collection.SemanticKey, semanticKey, StringComparison.Ordinal));
        }

        public ClientBufferedEditorState WithEditorKind(EditorPaneKind kind) {
            return new ClientBufferedEditorState {
                Kind = kind,
                BufferText = BufferText,
                CaretIndex = CaretIndex,
                ClientBufferRevision = Math.Max(0, ClientBufferRevision),
                AcceptedRemoteRevision = Math.Max(0, AcceptedRemoteRevision),
                Markers = [.. (Markers ?? []).Select(static marker => new ClientBufferedTextMarker {
                    Key = marker.Key,
                    VariantKey = marker.VariantKey,
                    StartIndex = Math.Max(0, marker.StartIndex),
                    Length = Math.Max(0, marker.Length),
                })],
                Selections = [.. (Selections ?? []).Select(static selection => new ClientBufferedEditorSelection {
                    SemanticKey = selection.SemanticKey,
                    ActiveItemId = selection.ActiveItemId,
                    ActiveOrdinal = Math.Max(0, selection.ActiveOrdinal),
                    SelectedItemIds = [.. (selection.SelectedItemIds ?? [])],
                })],
                Collections = [.. (Collections ?? []).Select(static collection => new ClientBufferedEditorCollection {
                    SemanticKey = collection.SemanticKey,
                    TotalItemCount = Math.Max(0, collection.TotalItemCount),
                    WindowOffset = Math.Max(0, collection.WindowOffset),
                    PageSize = Math.Max(0, collection.PageSize),
                })],
            };
        }

        public bool ContentEquals(ClientBufferedEditorState? other) {
            return ReferenceEquals(this, other)
                || other is not null
                && Kind == other.Kind
                && string.Equals(BufferText, other.BufferText, StringComparison.Ordinal)
                && CaretIndex == other.CaretIndex
                && ClientBufferRevision == other.ClientBufferRevision
                && AcceptedRemoteRevision == other.AcceptedRemoteRevision
                && SequenceEqual(Markers, other.Markers, MarkerEquals)
                && SequenceEqual(Selections, other.Selections, SelectionEquals)
                && SequenceEqual(Collections, other.Collections, CollectionEquals);
        }

        private static bool MarkerEquals(ClientBufferedTextMarker? left, ClientBufferedTextMarker? right) {
            return ReferenceEquals(left, right)
                || left is not null
                && right is not null
                && string.Equals(left.Key, right.Key, StringComparison.Ordinal)
                && string.Equals(left.VariantKey ?? string.Empty, right.VariantKey ?? string.Empty, StringComparison.Ordinal)
                && left.StartIndex == right.StartIndex
                && left.Length == right.Length;
        }

        private static bool SelectionEquals(ClientBufferedEditorSelection? left, ClientBufferedEditorSelection? right) {
            return ReferenceEquals(left, right)
                || left is not null
                && right is not null
                && string.Equals(left.SemanticKey, right.SemanticKey, StringComparison.Ordinal)
                && string.Equals(left.ActiveItemId, right.ActiveItemId, StringComparison.Ordinal)
                && left.ActiveOrdinal == right.ActiveOrdinal
                && SequenceEqual(left.SelectedItemIds, right.SelectedItemIds, static (x, y) => string.Equals(x, y, StringComparison.Ordinal));
        }

        private static bool CollectionEquals(ClientBufferedEditorCollection? left, ClientBufferedEditorCollection? right) {
            return ReferenceEquals(left, right)
                || left is not null
                && right is not null
                && string.Equals(left.SemanticKey, right.SemanticKey, StringComparison.Ordinal)
                && left.TotalItemCount == right.TotalItemCount
                && left.WindowOffset == right.WindowOffset
                && left.PageSize == right.PageSize;
        }

        private static bool SequenceEqual<T>(IReadOnlyList<T>? left, IReadOnlyList<T>? right, Func<T, T, bool> elementEquals) {
            if (ReferenceEquals(left, right)) {
                return true;
            }

            if (left is null || right is null || left.Count != right.Count) {
                return false;
            }

            for (var index = 0; index < left.Count; index++) {
                if (!elementEquals(left[index], right[index])) {
                    return false;
                }
            }

            return true;
        }
    }
}
