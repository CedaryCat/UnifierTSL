using Atelier.Session;
using System.Collections.Immutable;
using UnifierTSL.Contracts.Sessions;

namespace Atelier.Presentation.Window.Formatting {
    internal readonly record struct ReplFormatTrigger(string InsertedText, int InsertedStartIndex) {
        public bool IsNewLineInsertion {
            get {
                if (string.IsNullOrEmpty(InsertedText) || InsertedText[0] != '\n') {
                    return false;
                }

                for (var index = 1; index < InsertedText.Length; index++) {
                    if (InsertedText[index] is not (' ' or '\t')) {
                        return false;
                    }
                }

                return true;
            }
        }
    }

    internal readonly record struct DraftFormatResult(
        ClientBufferedEditorState State,
        DraftSnapshot Draft,
        ImmutableArray<SourceEditBatch> EditBatches);
}
