using System.Collections.Immutable;

namespace Atelier.Session {
    internal readonly record struct DraftRewrite(
        DraftSnapshot Draft,
        ImmutableArray<SourceEditBatch> EditBatches) {
        public static DraftRewrite Start(
            DraftSnapshot draft,
            ImmutableArray<SourceEditBatch> editBatches = default) {
            return new DraftRewrite(draft, editBatches.IsDefault ? [] : editBatches);
        }

        public DraftRewrite WithDraft(DraftSnapshot draft) {
            return this with { Draft = draft };
        }

        public DraftRewrite WithState(
            string sourceText,
            int sourceCaretIndex,
            VirtualPairLedger ledger,
            params IEnumerable<SourceEdit>[] editBatches) {
            var text = sourceText ?? string.Empty;
            var pairLedger = ledger ?? VirtualPairLedger.Empty;
            return new DraftRewrite(
                new DraftSnapshot(
                    text,
                    sourceCaretIndex,
                    null,
                    pairLedger),
                AppendEditBatches(editBatches));
        }

        public DraftRewrite Apply(SourceEdit edit, int? sourceCaretIndex = null) {
            var text = DraftMarkers.ApplySourceEditText(Draft.SourceText, edit);
            var ledger = Draft.PairLedger.ApplySourceEdit(edit);
            var caret = sourceCaretIndex ?? edit.Kind switch {
                SourceEditKind.VirtualMarkerDelete => edit.StartIndex,
                SourceEditKind.VirtualMarkerMaterialize => edit.StartIndex + edit.InsertedLength,
                _ => edit.StartIndex + edit.InsertedLength,
            };

            return WithState(text, Math.Clamp(caret, 0, text.Length), ledger, new[] { edit });
        }

        public DraftRewrite ApplyBatch(IEnumerable<SourceEdit> edits, int sourceCaretIndex) {
            var normalizedEdits = DraftMarkers.NormalizeSourceEdits(edits);
            var text = DraftMarkers.ApplySourceEditsText(Draft.SourceText, normalizedEdits);
            var ledger = Draft.PairLedger.ApplySourceEdits(normalizedEdits);
            return WithState(text, Math.Clamp(sourceCaretIndex, 0, text.Length), ledger, normalizedEdits);
        }

        public DraftRewrite RemovePairs(IEnumerable<long> pairIds, int? sourceCaretIndex = null) {
            var ledger = Draft.PairLedger;
            foreach (var pairId in pairIds ?? []) {
                ledger = ledger.RemovePair(pairId);
            }

            return WithState(
                Draft.SourceText,
                sourceCaretIndex ?? Draft.SourceCaretIndex,
                ledger);
        }

        private ImmutableArray<SourceEditBatch> AppendEditBatches(
            params IEnumerable<SourceEdit>[] editBatches) {
            var batches = SourceEditBatch.Create(editBatches);
            return EditBatches.IsDefaultOrEmpty
                ? batches
                : [.. EditBatches, .. batches];
        }
    }
}
