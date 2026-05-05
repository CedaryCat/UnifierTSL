using System.Collections.Immutable;
using System.Text;
using UnifierTSL.Contracts.Sessions;
using UnifierTSL.TextEditing;

namespace Atelier.Session {
    internal sealed record SourceTextMarker(
        string Key,
        string BaseKey,
        long PairId,
        int EncodedStartIndex,
        int EncodedLength,
        int SourceStartIndex,
        int SourceLength);

    internal readonly record struct EncodedDraft(
        string Text,
        int CaretIndex,
        ClientBufferedTextMarker[] Markers,
        VirtualPairLedger PairLedger);

    internal sealed class DraftSnapshot {
        public DraftSnapshot(
            string sourceText,
            int sourceCaretIndex,
            IReadOnlyList<SourceTextMarker>? sourceMarkers,
            VirtualPairLedger? pairLedger) {
            SourceText = sourceText ?? string.Empty;
            SourceCaretIndex = Math.Clamp(sourceCaretIndex, 0, SourceText.Length);
            PairLedger = pairLedger ?? VirtualPairLedger.Empty;
            SourceMarkers = sourceMarkers is null
                ? DraftMarkers.CreateSourceMarkers(PairLedger, SourceText.Length)
                : DraftMarkers.NormalizeSourceMarkers(sourceMarkers, SourceText.Length, PairLedger);
        }

        public string SourceText { get; }
        public int SourceCaretIndex { get; }
        public ImmutableArray<SourceTextMarker> SourceMarkers { get; }
        public VirtualPairLedger PairLedger { get; }

        public static DraftSnapshot Empty { get; } = new(string.Empty, 0, [], VirtualPairLedger.Empty);

        public DraftSnapshot With(
            string? sourceText = null,
            int? sourceCaretIndex = null,
            IReadOnlyList<SourceTextMarker>? sourceMarkers = null,
            VirtualPairLedger? pairLedger = null) {
            var text = sourceText ?? SourceText;
            var ledger = pairLedger ?? PairLedger;
            return new DraftSnapshot(
                text,
                sourceCaretIndex ?? SourceCaretIndex,
                sourceMarkers ?? DraftMarkers.CreateSourceMarkers(ledger, text.Length),
                ledger);
        }
    }

    internal readonly record struct SourceEdit(
        int StartIndex,
        int RemovedLength,
        string InsertedText,
        SourceEditKind Kind = SourceEditKind.Text,
        long PairId = 0) {
        public int EndIndex => StartIndex + Math.Max(0, RemovedLength);
        public int InsertedLength => InsertedText?.Length ?? 0;
        public int Delta => InsertedLength - Math.Max(0, RemovedLength);
        public bool IsInsertion => RemovedLength == 0 && InsertedLength > 0;
        public bool IsDeletion => RemovedLength > 0 && InsertedLength == 0;
    }

    internal readonly record struct SourceEditBatch {
        public SourceEditBatch(IEnumerable<SourceEdit>? edits) {
            Edits = DraftMarkers.NormalizeSourceEdits(edits);
        }

        public ImmutableArray<SourceEdit> Edits { get; }
        public bool IsEmpty => Edits.IsDefaultOrEmpty;

        public static ImmutableArray<SourceEditBatch> Create(params IEnumerable<SourceEdit>[] editBatches) {
            return [.. editBatches
                .Select(static edits => new SourceEditBatch(edits))
                .Where(static batch => !batch.IsEmpty)];
        }
    }

    internal enum SourceEditKind {
        Text,
        VirtualMarkerDelete,
        VirtualMarkerMaterialize,
    }

    internal sealed record VirtualPairLedgerEntry(
        long PairId,
        VirtualPairKind Kind,
        string MarkerKey,
        int OpenerIndex,
        int CloserIndex,
        int CloserLength,
        int EncodedStartIndex,
        int EncodedLength) {
        public int CloserEndIndex => CloserIndex + CloserLength;
    }

    internal sealed class VirtualPairLedger {
        public VirtualPairLedger(long nextPairId, IReadOnlyList<VirtualPairLedgerEntry>? entries) {
            NextPairId = Math.Max(1, nextPairId);
            Entries = NormalizeEntries(entries);
        }

        public long NextPairId { get; }
        public ImmutableArray<VirtualPairLedgerEntry> Entries { get; }

        public static VirtualPairLedger Empty { get; } = new(1, []);

        public VirtualPairLedger CreatePair(
            VirtualPairKind kind,
            string markerKey,
            int openerIndex,
            int closerIndex,
            int closerLength,
            out VirtualPairLedgerEntry entry) {
            entry = new VirtualPairLedgerEntry(
                NextPairId,
                kind,
                DraftMarkers.GetBaseKey(markerKey),
                Math.Max(0, openerIndex),
                Math.Max(0, closerIndex),
                Math.Max(0, closerLength),
                0,
                0);
            return new VirtualPairLedger(checked(NextPairId + 1), [.. Entries, entry]);
        }

        public VirtualPairLedger RemovePair(long pairId) {
            return new VirtualPairLedger(NextPairId, [.. Entries.Where(entry => entry.PairId != pairId)]);
        }

        public VirtualPairLedger ApplyEncodedProjection(IReadOnlyList<SourceTextMarker>? sourceMarkers) {
            Dictionary<long, SourceTextMarker> markersByPairId = [];
            foreach (var marker in sourceMarkers ?? []) {
                if (marker.PairId > 0) {
                    markersByPairId[marker.PairId] = marker;
                }
            }

            return new VirtualPairLedger(NextPairId, [.. Entries.Select(entry => markersByPairId.TryGetValue(entry.PairId, out var marker)
                ? entry with {
                    EncodedStartIndex = marker.EncodedStartIndex,
                    EncodedLength = marker.EncodedLength,
                }
                : entry)]);
        }

        public VirtualPairLedger ApplySourceEdit(SourceEdit edit) {
            var start = Math.Max(0, edit.StartIndex);
            var removed = Math.Max(0, edit.RemovedLength);
            var end = start + removed;
            var delta = edit.Delta;
            List<VirtualPairLedgerEntry> updated = [];
            foreach (var entry in Entries) {
                if (edit.PairId == entry.PairId
                    && edit.Kind is SourceEditKind.VirtualMarkerDelete or SourceEditKind.VirtualMarkerMaterialize) {
                    continue;
                }

                if (removed > 0 && start <= entry.OpenerIndex && end > entry.OpenerIndex) {
                    continue;
                }

                if (removed > 0 && start < entry.CloserEndIndex && end > entry.CloserIndex) {
                    continue;
                }

                if (end <= entry.OpenerIndex) {
                    updated.Add(entry with {
                        OpenerIndex = entry.OpenerIndex + delta,
                        CloserIndex = entry.CloserIndex + delta,
                    });
                    continue;
                }

                if (start <= entry.OpenerIndex && removed == 0) {
                    updated.Add(entry with {
                        OpenerIndex = entry.OpenerIndex + delta,
                        CloserIndex = entry.CloserIndex + delta,
                    });
                    continue;
                }

                if (end <= entry.CloserIndex) {
                    updated.Add(entry with { CloserIndex = entry.CloserIndex + delta });
                    continue;
                }

                updated.Add(entry);
            }

            return new VirtualPairLedger(NextPairId, updated);
        }

        public VirtualPairLedger ApplySourceEdits(IEnumerable<SourceEdit>? edits) {
            var ledger = this;
            foreach (var edit in DraftMarkers.NormalizeSourceEdits(edits)
                .OrderByDescending(static edit => edit.StartIndex)) {
                ledger = ledger.ApplySourceEdit(edit);
            }

            return ledger;
        }

        public VirtualPairLedger ToSnapshot() {
            return new VirtualPairLedger(NextPairId, Entries);
        }

        private static ImmutableArray<VirtualPairLedgerEntry> NormalizeEntries(IReadOnlyList<VirtualPairLedgerEntry>? entries) {
            return [.. (entries ?? [])
                .Where(static entry => entry is not null && entry.PairId > 0 && entry.CloserLength > 0)
                .GroupBy(static entry => entry.PairId)
                .Select(static group => group.First())
                .OrderBy(static entry => entry.PairId)];
        }
    }

    internal sealed class DecodedDraft(
        string sourceText,
        int sourceCaretIndex,
        ImmutableArray<SourceTextMarker> sourceMarkers) {
        public string SourceText { get; } = sourceText ?? string.Empty;
        public int SourceCaretIndex { get; } = Math.Clamp(sourceCaretIndex, 0, sourceText?.Length ?? 0);
        public ImmutableArray<SourceTextMarker> SourceMarkers { get; } = sourceMarkers.IsDefault ? [] : sourceMarkers;
    }

    internal static class DraftMarkers {
        public const string CloseParenKey = "atelier.marker.close-paren";
        public const string CloseBracketKey = "atelier.marker.close-bracket";
        public const string CloseBraceKey = "atelier.marker.close-brace";
        public const string CloseAngleKey = "atelier.marker.close-angle";
        public const string DoubleQuoteKey = "atelier.marker.double-quote";
        public const string SingleQuoteKey = "atelier.marker.single-quote";

        public static ImmutableArray<string> Keys { get; } = [
            CloseParenKey,
            CloseBracketKey,
            CloseBraceKey,
            CloseAngleKey,
            DoubleQuoteKey,
            SingleQuoteKey,
        ];

        public static DraftSnapshot DecodeSnapshot(string? encodedText, IReadOnlyList<ClientBufferedTextMarker>? markers, int encodedCaretIndex) {
            var draft = Decode(encodedText, markers, encodedCaretIndex);
            return new DraftSnapshot(draft.SourceText, draft.SourceCaretIndex, draft.SourceMarkers, VirtualPairLedger.Empty);
        }

        public static DecodedDraft Decode(string? encodedText, IReadOnlyList<ClientBufferedTextMarker>? markers, int encodedCaretIndex) {
            return DecodeCore(encodedText, markers, encodedCaretIndex, expandMarkers: true);
        }

        public static DraftSnapshot RecoverWithoutVirtualPairs(
            string? encodedText,
            IReadOnlyList<ClientBufferedTextMarker>? markers,
            int encodedCaretIndex) {
            var draft = DecodeCore(encodedText, markers, encodedCaretIndex, expandMarkers: false);
            return new DraftSnapshot(draft.SourceText, draft.SourceCaretIndex, [], VirtualPairLedger.Empty);
        }

        private static DecodedDraft DecodeCore(
            string? encodedText,
            IReadOnlyList<ClientBufferedTextMarker>? markers,
            int encodedCaretIndex,
            bool expandMarkers) {
            var text = encodedText ?? string.Empty;
            var normalizedMarkers = EditorTextMarkerOps.Normalize(markers, text.Length);
            var boundedCaret = Math.Clamp(encodedCaretIndex, 0, text.Length);
            var sourceBuilder = new StringBuilder(text.Length);
            List<SourceTextMarker> sourceMarkers = [];
            var encodedIndex = 0;
            var sourceCaret = 0;
            foreach (var marker in normalizedMarkers) {
                if (boundedCaret >= encodedIndex && boundedCaret <= marker.StartIndex) {
                    sourceCaret = sourceBuilder.Length + boundedCaret - encodedIndex;
                }

                if (marker.StartIndex > encodedIndex) {
                    sourceBuilder.Append(text, encodedIndex, marker.StartIndex - encodedIndex);
                }

                var markerEnd = marker.StartIndex + marker.Length;
                if (expandMarkers) {
                    var sourceStart = sourceBuilder.Length;
                    var sourceText = ResolveSourceText(marker.Key, text, marker);
                    var baseKey = GetBaseKey(marker.Key);
                    sourceBuilder.Append(sourceText);
                    sourceMarkers.Add(new SourceTextMarker(
                        baseKey,
                        baseKey,
                        0,
                        marker.StartIndex,
                        marker.Length,
                        sourceStart,
                        sourceText.Length));
                    encodedIndex = markerEnd;

                    if (boundedCaret > marker.StartIndex && boundedCaret < encodedIndex) {
                        sourceCaret = sourceStart;
                    }
                    else if (boundedCaret == encodedIndex) {
                        sourceCaret = sourceStart + sourceText.Length;
                    }
                    continue;
                }

                if (boundedCaret > marker.StartIndex && boundedCaret < markerEnd) {
                    sourceCaret = sourceBuilder.Length;
                }

                encodedIndex = markerEnd;
            }

            if (encodedIndex < text.Length) {
                if (boundedCaret >= encodedIndex) {
                    sourceCaret = sourceBuilder.Length + boundedCaret - encodedIndex;
                }

                sourceBuilder.Append(text, encodedIndex, text.Length - encodedIndex);
            }

            return new DecodedDraft(
                sourceBuilder.ToString(),
                sourceCaret,
                expandMarkers ? [.. sourceMarkers] : []);
        }

        public static EncodedDraft Encode(DraftSnapshot snapshot) {
            return Encode(snapshot.SourceText, snapshot.SourceMarkers, snapshot.SourceCaretIndex, snapshot.PairLedger);
        }

        public static EncodedDraft Encode(
            string? sourceText,
            IReadOnlyList<SourceTextMarker>? sourceMarkers,
            int sourceCaretIndex,
            VirtualPairLedger? pairLedger = null) {
            var normalizedSource = sourceText ?? string.Empty;
            var ledger = pairLedger ?? VirtualPairLedger.Empty;
            var normalizedMarkers = NormalizeSourceMarkers(sourceMarkers, normalizedSource.Length, ledger);
            var boundedCaret = Math.Clamp(sourceCaretIndex, 0, normalizedSource.Length);
            var encodedBuilder = new StringBuilder(normalizedSource.Length);
            List<ClientBufferedTextMarker> encodedMarkers = [];
            List<SourceTextMarker> encodedSourceMarkers = [];
            var sourceIndex = 0;
            var encodedCaret = 0;
            foreach (var marker in normalizedMarkers) {
                if (boundedCaret >= sourceIndex && boundedCaret <= marker.SourceStartIndex) {
                    encodedCaret = encodedBuilder.Length + boundedCaret - sourceIndex;
                }

                if (marker.SourceStartIndex > sourceIndex) {
                    encodedBuilder.Append(normalizedSource, sourceIndex, marker.SourceStartIndex - sourceIndex);
                }

                var encodedStart = encodedBuilder.Length;
                var placeholder = ResolvePlaceholder(marker.BaseKey);
                encodedBuilder.Append(placeholder);
                encodedMarkers.Add(new ClientBufferedTextMarker {
                    Key = marker.BaseKey,
                    StartIndex = encodedStart,
                    Length = placeholder.Length,
                });
                encodedSourceMarkers.Add(marker with {
                    Key = marker.BaseKey,
                    BaseKey = marker.BaseKey,
                    EncodedStartIndex = encodedStart,
                    EncodedLength = placeholder.Length,
                });
                sourceIndex = marker.SourceStartIndex + marker.SourceLength;

                if (boundedCaret > marker.SourceStartIndex && boundedCaret < sourceIndex) {
                    encodedCaret = encodedStart;
                }
                else if (boundedCaret == sourceIndex) {
                    encodedCaret = encodedBuilder.Length;
                }
            }

            if (sourceIndex < normalizedSource.Length) {
                if (boundedCaret >= sourceIndex) {
                    encodedCaret = encodedBuilder.Length + boundedCaret - sourceIndex;
                }

                encodedBuilder.Append(normalizedSource, sourceIndex, normalizedSource.Length - sourceIndex);
            }

            return new EncodedDraft(
                encodedBuilder.ToString(),
                encodedCaret,
                [.. encodedMarkers],
                ledger.ApplyEncodedProjection(encodedSourceMarkers));
        }

        public static ImmutableArray<SourceTextMarker> CreateSourceMarkers(
            VirtualPairLedger? ledger,
            int sourceTextLength) {
            var textLength = Math.Max(0, sourceTextLength);
            return [.. (ledger ?? VirtualPairLedger.Empty).Entries
                .Select(entry => new SourceTextMarker(
                    GetBaseKey(entry.MarkerKey),
                    GetBaseKey(entry.MarkerKey),
                    entry.PairId,
                    entry.EncodedStartIndex,
                    entry.EncodedLength,
                    Math.Clamp(entry.CloserIndex, 0, textLength),
                    Math.Clamp(entry.CloserLength, 0, textLength - Math.Clamp(entry.CloserIndex, 0, textLength))))
                .Where(static marker => marker.SourceLength > 0)
                .OrderBy(static marker => marker.SourceStartIndex)
                .ThenBy(static marker => marker.PairId)];
        }

        public static ImmutableArray<SourceTextMarker> NormalizeSourceMarkers(
            IReadOnlyList<SourceTextMarker>? sourceMarkers,
            int sourceTextLength,
            VirtualPairLedger? ledger = null) {
            var textLength = Math.Max(0, sourceTextLength);
            Dictionary<long, VirtualPairLedgerEntry> ledgerEntries = [];
            foreach (var entry in (ledger ?? VirtualPairLedger.Empty).Entries) {
                ledgerEntries[entry.PairId] = entry;
            }

            List<SourceTextMarker> normalized = [];
            foreach (var marker in sourceMarkers ?? []) {
                if (marker is null) {
                    continue;
                }

                var pairId = Math.Max(0, marker.PairId);
                var baseKey = GetBaseKey(marker.BaseKey);
                var start = Math.Clamp(marker.SourceStartIndex, 0, textLength);
                var length = Math.Clamp(marker.SourceLength, 0, textLength - start);
                if (pairId > 0 && ledgerEntries.TryGetValue(pairId, out var entry)) {
                    baseKey = GetBaseKey(entry.MarkerKey);
                    start = Math.Clamp(entry.CloserIndex, 0, textLength);
                    length = Math.Clamp(entry.CloserLength, 0, textLength - start);
                }

                if (pairId <= 0 || string.IsNullOrWhiteSpace(baseKey) || length <= 0 || !TryGetSourceText(baseKey, out _)) {
                    continue;
                }

                normalized.Add(new SourceTextMarker(
                    baseKey,
                    baseKey,
                    pairId,
                    Math.Max(0, marker.EncodedStartIndex),
                    Math.Max(0, marker.EncodedLength),
                    start,
                    length));
            }

            return [.. normalized
                .GroupBy(static marker => marker.PairId)
                .Select(static group => group.First())
                .OrderBy(static marker => marker.SourceStartIndex)
                .ThenBy(static marker => marker.PairId)];
        }

        public static string ResolvePlaceholder(string key) {
            return "<!" + GetBaseKey(key) + ">";
        }

        public static bool TryGetSourceText(string key, out string sourceText) {
            sourceText = GetBaseKey(key) switch {
                CloseParenKey => ")",
                CloseBracketKey => "]",
                CloseBraceKey => "}",
                CloseAngleKey => ">",
                DoubleQuoteKey => "\"",
                SingleQuoteKey => "'",
                _ => string.Empty,
            };
            return sourceText.Length > 0;
        }

        public static int MapSourcePosition(
            IReadOnlyList<SourceTextMarker>? sourceMarkers,
            int sourceTextLength,
            int sourcePosition,
            bool preferEnd) {
            var boundedSourcePosition = Math.Clamp(sourcePosition, 0, Math.Max(0, sourceTextLength));
            var encodedIndex = 0;
            var sourceIndex = 0;
            foreach (var marker in NormalizeSourceMarkers(sourceMarkers, sourceTextLength)) {
                if (boundedSourcePosition < marker.SourceStartIndex) {
                    return encodedIndex + boundedSourcePosition - sourceIndex;
                }

                encodedIndex += marker.EncodedStartIndex - encodedIndex;
                sourceIndex = marker.SourceStartIndex;
                if (boundedSourcePosition == marker.SourceStartIndex) {
                    return preferEnd
                        ? marker.EncodedStartIndex + marker.EncodedLength
                        : marker.EncodedStartIndex;
                }

                var markerSourceEnd = marker.SourceStartIndex + marker.SourceLength;
                if (boundedSourcePosition > marker.SourceStartIndex && boundedSourcePosition < markerSourceEnd) {
                    return preferEnd
                        ? marker.EncodedStartIndex + marker.EncodedLength
                        : marker.EncodedStartIndex;
                }

                if (boundedSourcePosition == markerSourceEnd) {
                    return marker.EncodedStartIndex + marker.EncodedLength;
                }

                encodedIndex = marker.EncodedStartIndex + marker.EncodedLength;
                sourceIndex = marker.SourceStartIndex + marker.SourceLength;
            }

            return encodedIndex + boundedSourcePosition - sourceIndex;
        }

        public static bool TryMapSourceSpan(
            IReadOnlyList<SourceTextMarker>? sourceMarkers,
            int sourceTextLength,
            int startIndex,
            int length,
            out int encodedStart,
            out int encodedLength) {
            var boundedStart = Math.Clamp(startIndex, 0, Math.Max(0, sourceTextLength));
            var boundedEnd = Math.Clamp(startIndex + Math.Max(0, length), boundedStart, Math.Max(0, sourceTextLength));
            encodedStart = MapSourcePosition(sourceMarkers, sourceTextLength, boundedStart, preferEnd: false);
            var encodedEnd = MapSourcePosition(sourceMarkers, sourceTextLength, boundedEnd, preferEnd: true);
            encodedLength = Math.Max(0, encodedEnd - encodedStart);
            return encodedLength > 0 || length == 0;
        }

        public static string ApplySourceEditText(string text, SourceEdit edit) {
            var source = text ?? string.Empty;
            var start = Math.Clamp(edit.StartIndex, 0, source.Length);
            var removed = Math.Clamp(edit.RemovedLength, 0, source.Length - start);
            return source.Remove(start, removed).Insert(start, edit.InsertedText ?? string.Empty);
        }

        public static string ApplySourceEditsText(string text, IEnumerable<SourceEdit>? edits) {
            var source = text ?? string.Empty;
            foreach (var edit in NormalizeSourceEdits(edits).OrderByDescending(static edit => edit.StartIndex)) {
                source = ApplySourceEditText(source, edit);
            }

            return source;
        }

        public static ImmutableArray<SourceEdit> NormalizeSourceEdits(IEnumerable<SourceEdit>? edits) {
            return [.. (edits ?? [])
                .Where(static edit => edit.RemovedLength > 0 || edit.InsertedLength > 0)
                .GroupBy(static edit => new {
                    edit.StartIndex,
                    edit.RemovedLength,
                    InsertedText = edit.InsertedText ?? string.Empty,
                    edit.Kind,
                    edit.PairId,
                })
                .Select(static group => group.First())
                .OrderBy(static edit => edit.StartIndex)
                .ThenBy(static edit => edit.RemovedLength)];
        }

        public static int MapPositionThroughSourceEdits(
            int position,
            IEnumerable<SourceEdit>? edits,
            bool preferEnd = false) {
            var boundedPosition = Math.Max(0, position);
            var delta = 0;
            foreach (var edit in NormalizeSourceEdits(edits)) {
                var start = Math.Max(0, edit.StartIndex);
                var removed = Math.Max(0, edit.RemovedLength);
                var end = start + removed;
                if (boundedPosition < start) {
                    break;
                }

                if (removed == 0) {
                    if (boundedPosition == start) {
                        return Math.Max(0, start + delta + (preferEnd ? edit.InsertedLength : 0));
                    }

                    delta += edit.Delta;
                    continue;
                }

                if (boundedPosition < end) {
                    return Math.Max(0, start + delta + (preferEnd ? edit.InsertedLength : 0));
                }

                if (boundedPosition == end) {
                    return Math.Max(0, start + delta + (preferEnd ? edit.InsertedLength : 0));
                }

                delta += edit.Delta;
            }

            return Math.Max(0, boundedPosition + delta);
        }

        public static bool TryMapSourceSpanThroughSourceEdits(
            int startIndex,
            int length,
            IEnumerable<SourceEdit>? edits,
            out int mappedStart,
            out int mappedLength) {
            mappedStart = 0;
            mappedLength = 0;
            var start = Math.Max(0, startIndex);
            var spanLength = Math.Max(0, length);
            if (spanLength == 0) {
                return false;
            }

            var end = start + spanLength;
            var normalizedEdits = NormalizeSourceEdits(edits);
            foreach (var edit in normalizedEdits) {
                var editStart = Math.Max(0, edit.StartIndex);
                var editEnd = editStart + Math.Max(0, edit.RemovedLength);
                var touchesSpan = edit.RemovedLength == 0
                    ? editStart > start && editStart < end
                    : editStart < end && editEnd > start;
                if (touchesSpan) {
                    return false;
                }
            }

            mappedStart = MapPositionThroughSourceEdits(start, normalizedEdits, preferEnd: true);
            var mappedEnd = MapPositionThroughSourceEdits(end, normalizedEdits);
            mappedLength = Math.Max(0, mappedEnd - mappedStart);
            return mappedLength > 0;
        }

        public static string GetBaseKey(string? key) {
            return key ?? string.Empty;
        }

        private static string ResolveSourceText(string key, string encodedText, ClientBufferedTextMarker marker) {
            if (TryGetSourceText(key, out var sourceText)) {
                return sourceText;
            }

            var start = Math.Clamp(marker.StartIndex, 0, encodedText.Length);
            var length = Math.Clamp(marker.Length, 0, encodedText.Length - start);
            return length <= 0 ? string.Empty : encodedText.Substring(start, length);
        }
    }
}
