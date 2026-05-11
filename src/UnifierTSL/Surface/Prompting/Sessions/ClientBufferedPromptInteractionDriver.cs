using UnifierTSL.Surface.Prompting.Runtime;
using UnifierTSL.Surface.Prompting;
using UnifierTSL.Surface.Prompting.Model;
using UnifierTSL.Contracts.Projection;
using UnifierTSL.Contracts.Sessions;
using UnifierTSL.Contracts.Terminal;
using UnifierTSL.TextEditing;

namespace UnifierTSL.Surface.Prompting.Sessions;
    internal readonly record struct ClientBufferedPromptInteractionDriverOptions(
        PromptSurfaceProjectionOptions RenderOptions,
        PromptBufferedAuthoringOptions BufferedAuthoring,
        long InitialClientBufferRevision = 1);

    internal readonly record struct ClientBufferedPromptInteractionState(
        PromptInteractionState InteractionState,
        ProjectionDocumentContent PublicationContent,
        long ClientBufferRevision);

    internal sealed class ClientBufferedPromptInteractionDriver
    {
        private readonly PromptSurfaceCompiler compiler;
        private readonly PromptInteractionRunner sessionRunner;
        private readonly PromptBufferedAuthoringOptions bufferedAuthoring;
        private readonly EditorPaneKind editorKind;
        private readonly bool analyzeCurrentLogicalLine;
        private readonly PromptMultilineDisplayHighlightCache? multilineDisplayHighlights;
        private CompletionRenderState lastPublishedCompletionState;
        private string[] lastPublishedInterpretationOptionIds;
        private string lastPublishedContentSignature;
        private long clientBufferRevision;

        public ClientBufferedPromptInteractionDriver(
            PromptSurfaceCompiler compiler,
            ClientBufferedPromptInteractionDriverOptions options) {

            this.compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
            sessionRunner = new PromptInteractionRunner(this.compiler, options.RenderOptions);
            bufferedAuthoring = options.BufferedAuthoring;
            editorKind = bufferedAuthoring.EditorKind;
            analyzeCurrentLogicalLine = bufferedAuthoring.AnalyzeCurrentLogicalLine;
            multilineDisplayHighlights = analyzeCurrentLogicalLine && editorKind == EditorPaneKind.MultiLine
                ? new PromptMultilineDisplayHighlightCache(this.compiler)
                : null;
            clientBufferRevision = Math.Max(0, options.InitialClientBufferRevision);

            var initialReactiveState = sessionRunner.Current.InputState.CopyNormalized();
            Current = CreateState(sessionRunner.Current, initialReactiveState);
            var initialPublicationState = CreatePublicationState(Current);
            lastPublishedCompletionState = initialPublicationState.Completion;
            lastPublishedInterpretationOptionIds = initialPublicationState.InterpretationOptionIds;
            lastPublishedContentSignature = initialPublicationState.Signature;
        }

        public ClientBufferedPromptInteractionState Current { get; private set; }

        public bool TryUpdate(ClientBufferedEditorState reactiveState, out ClientBufferedPromptInteractionState state) {
            return TryUpdateCore(CreatePromptInputState(reactiveState), reactiveState.ClientBufferRevision, out state);
        }

        public bool TryRefreshRuntimeDependencies(out ClientBufferedPromptInteractionState state) {
            var authorityReactiveState = Current.InteractionState.InputState.CopyNormalized();
            if (!sessionRunner.TryRefreshRuntimeDependencies(out var sessionState)) {
                state = Current;
                return false;
            }

            return TryPublishState(
                CreateState(
                    sessionState,
                    authorityReactiveState,
                    Current.ClientBufferRevision,
                    refreshDisplayHighlights: true),
                forcePublication: true,
                out state);
        }

        private bool TryUpdateCore(
            PromptInputState authorityReactiveState,
            long authorityClientBufferRevision,
            out ClientBufferedPromptInteractionState state) {
            return TryPublishState(
                CreateState(
                    sessionRunner.Update(ProjectReactiveState(authorityReactiveState)),
                    authorityReactiveState,
                    authorityClientBufferRevision),
                forcePublication: false,
                out state);
        }

        private bool TryPublishState(
            ClientBufferedPromptInteractionState nextState,
            bool forcePublication,
            out ClientBufferedPromptInteractionState state) {
            Current = nextState;
            state = nextState;
            var publicationState = CreatePublicationState(nextState);
            if (!forcePublication
                && string.Equals(lastPublishedContentSignature, publicationState.Signature, StringComparison.Ordinal)) {
                return false;
            }

            lastPublishedCompletionState = publicationState.Completion;
            lastPublishedInterpretationOptionIds = publicationState.InterpretationOptionIds;
            lastPublishedContentSignature = publicationState.Signature;
            clientBufferRevision = nextState.ClientBufferRevision;
            return true;
        }

        private ClientBufferedPromptInteractionState CreateState(
            PromptInteractionState sessionState,
            PromptInputState authorityReactiveState,
            long? authorityClientBufferRevision = null,
            bool refreshDisplayHighlights = false) {
            var renderSessionState = RebaseSessionState(
                sessionState,
                authorityReactiveState,
                refreshDisplayHighlights);
            var nextClientBufferRevision = Math.Max(clientBufferRevision, Math.Max(0, authorityClientBufferRevision ?? clientBufferRevision));
            if (authorityClientBufferRevision is not null
                && Current.InteractionState.InputState is { } publishedInputState
                && !authorityReactiveState.ContentEquals(publishedInputState)) {
                nextClientBufferRevision = checked(nextClientBufferRevision + 1);
            }

            var publicationContent = PromptProjectionDocumentFactory.CreatePublishedContent(
                renderSessionState.Content,
                renderSessionState.Computation,
                renderSessionState.CandidateWindow,
                renderSessionState.InputState,
                bufferedAuthoring,
                nextClientBufferRevision);
            return new ClientBufferedPromptInteractionState(
                renderSessionState,
                publicationContent,
                nextClientBufferRevision);
        }

        private PromptInputState ProjectReactiveState(PromptInputState reactiveState) {
            if (!analyzeCurrentLogicalLine) {
                return reactiveState;
            }

            return PromptLogicalLineProjection.Create(reactiveState.InputText, reactiveState.CursorIndex)
                .Project(reactiveState);
        }

        private PromptInteractionState RebaseSessionState(
            PromptInteractionState sessionState,
            PromptInputState authorityReactiveState,
            bool refreshDisplayHighlights) {
            if (!analyzeCurrentLogicalLine) {
                return sessionState;
            }

            var projection = PromptLogicalLineProjection.Create(
                authorityReactiveState.InputText,
                authorityReactiveState.CursorIndex);
            var rebasedState = projection.Rebase(sessionState);
            return ApplyDisplayHighlights(
                rebasedState,
                authorityReactiveState,
                projection,
                refreshDisplayHighlights);
        }

        private PromptInteractionState ApplyDisplayHighlights(
            PromptInteractionState sessionState,
            PromptInputState authorityReactiveState,
            PromptLogicalLineProjection projection,
            bool refreshDisplayHighlights) {
            if (multilineDisplayHighlights is null
                || (authorityReactiveState.InputText ?? string.Empty).IndexOfAny(['\r', '\n']) < 0) {
                return sessionState;
            }

            var overlayHighlights = multilineDisplayHighlights.Resolve(
                authorityReactiveState,
                projection,
                sessionState.Computation.InputHighlights,
                refreshDisplayHighlights);
            if (overlayHighlights.Length == 0) {
                return sessionState;
            }

            var mergedHighlights = sessionState.Computation.InputHighlights
                .Concat(overlayHighlights)
                .OrderBy(static span => span.StartIndex)
                .ThenBy(static span => span.Length)
                .ToArray();
            var highlightedComputation = sessionState.Computation with {
                InputHighlights = mergedHighlights,
            };
            return new PromptInteractionState(
                sessionState.Purpose,
                sessionState.InputState,
                sessionState.Content,
                highlightedComputation,
                sessionState.CandidateWindow);
        }

        private static (CompletionRenderState Completion, string[] InterpretationOptionIds, string Signature) CreatePublicationState(
            ClientBufferedPromptInteractionState state) {
            var sessionState = state.InteractionState;
            var completion = CreateCompletionRenderState(sessionState);
            string[] interpretationOptionIds = [.. (sessionState.Computation.InterpretationState.Interpretations ?? [])
                .Select(static interpretation => interpretation.Id)
                .Where(static optionId => !string.IsNullOrWhiteSpace(optionId))];
            return (completion, interpretationOptionIds, string.Concat(
                Math.Max(0, state.ClientBufferRevision),
                "\u001f",
                completion.SelectedItemId,
                "\u001f",
                ResolveActiveInterpretationId(sessionState.Computation.InterpretationState)));
        }

        private static string ResolveActiveInterpretationId(PromptInterpretationState interpretationState) {
            var interpretations = interpretationState.Interpretations ?? [];
            var activeIndex = !string.IsNullOrWhiteSpace(interpretationState.ActiveInterpretationId)
                ? Array.FindIndex(interpretations, interpretation =>
                    string.Equals(interpretation.Id, interpretationState.ActiveInterpretationId, StringComparison.Ordinal))
                : -1;
            if (activeIndex < 0
                && interpretationState.ActiveInterpretationIndex >= 0
                && interpretationState.ActiveInterpretationIndex < interpretations.Length) {
                activeIndex = interpretationState.ActiveInterpretationIndex;
            }
            if (activeIndex < 0) {
                activeIndex = interpretations.Length == 0 ? -1 : 0;
            }
            return activeIndex < 0 ? string.Empty : interpretations[activeIndex].Id;
        }

        private static CompletionRenderState CreateCompletionRenderState(PromptInteractionState sessionState) {
            var displayedCandidates = PromptCandidateWindowProjector.ResolveDisplayedCandidates(
                sessionState.Computation,
                sessionState.CandidateWindow);
            var itemIds = ResolveCompletionItemIds(displayedCandidates, sessionState.InputState.InputText);
            var selectedItemIndex = sessionState.CandidateWindow.IsPaged
                ? Math.Max(0, sessionState.CandidateWindow.SelectedWindowIndex) - 1
                : sessionState.InputState.CompletionIndex - 1;
            return new CompletionRenderState(
                sessionState.CandidateWindow.IsPaged,
                sessionState.CandidateWindow.IsPaged
                    ? Math.Max(0, sessionState.CandidateWindow.TotalCandidateCount)
                    : displayedCandidates.Length,
                Math.Max(0, sessionState.CandidateWindow.WindowOffset),
                selectedItemIndex >= 0 && selectedItemIndex < itemIds.Length
                    ? itemIds[selectedItemIndex]
                    : string.Empty,
                itemIds);
        }

        private static string[] ResolveCompletionItemIds(IReadOnlyList<PromptCompletionItem> candidates, string? sourceText) {
            return [.. (candidates ?? [])
                .Select(candidate => ResolveCompletionItemId(candidate, sourceText))
                .Where(static itemId => !string.IsNullOrWhiteSpace(itemId))];
        }

        private static string ResolveCompletionItemId(PromptCompletionItem candidate, string? sourceText) {
            if (!string.IsNullOrWhiteSpace(candidate.Id)) {
                return candidate.Id;
            }

            var resolved = candidate.PrimaryEdit.Apply(sourceText ?? string.Empty);
            return string.IsNullOrWhiteSpace(resolved)
                ? candidate.DisplayText ?? string.Empty
                : resolved;
        }

        private PromptInputState CreatePromptInputState(ClientBufferedEditorState reactiveState) {
            string bufferText = reactiveState.BufferText ?? string.Empty;
            int caretIndex = Math.Clamp(reactiveState.CaretIndex, 0, bufferText.Length);
            var completionSelection = reactiveState.FindSelection(EditorProjectionSemanticKeys.AssistPrimaryList);
            var completionCollection = reactiveState.FindCollection(EditorProjectionSemanticKeys.AssistPrimaryList);
            int completionIndex = ResolveCompletionOrdinal(completionSelection);
            int completionCount = completionCollection is null
                ? completionIndex > 0 ? Math.Max(0, lastPublishedCompletionState.TotalItemCount) : 0
                : Math.Max(0, completionCollection.TotalItemCount);
            int completionWindowOffset = completionCollection is null
                ? completionIndex > 0 ? Math.Max(0, lastPublishedCompletionState.WindowOffset) : 0
                : Math.Max(0, completionCollection.WindowOffset);
            return new PromptInputState {
                InputText = bufferText,
                CursorIndex = caretIndex,
                CompletionIndex = completionIndex,
                CompletionCount = completionCount,
                CandidateWindowOffset = completionWindowOffset,
                PreferredCompletionText = reactiveState.FindSelection(EditorProjectionSemanticKeys.InputGhost)?.ActiveItemId ?? string.Empty,
                PreferredInterpretationId = ResolveInterpretationId(
                    reactiveState.FindSelection(EditorProjectionSemanticKeys.AssistSecondaryList)),
            }.Normalize(
                trimPreferredInterpretationId: true,
                normalizePreferredCompletionText: false);
        }

        private int ResolveCompletionOrdinal(ClientBufferedEditorSelection? selection) {
            if (selection is null) {
                return 0;
            }

            if (selection.ActiveOrdinal > 0) {
                return selection.ActiveOrdinal;
            }

            if (string.IsNullOrWhiteSpace(selection.ActiveItemId)) {
                return 0;
            }

            return ResolveCompletionOrdinal(lastPublishedCompletionState, selection.ActiveItemId);
        }

        private static int ResolveCompletionOrdinal(
            CompletionRenderState completionState,
            string completionItemId) {
            var itemIds = completionState.ItemIds ?? [];
            for (var index = 0; index < itemIds.Length; index++) {
                if (!string.Equals(itemIds[index], completionItemId, StringComparison.Ordinal)) {
                    continue;
                }

                return completionState.IsPaged
                    ? Math.Max(0, completionState.WindowOffset) + index + 1
                    : index + 1;
            }

            return 0;
        }

        private string ResolveInterpretationId(ClientBufferedEditorSelection? selection) {
            if (selection is null) {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(selection.ActiveItemId)) {
                return selection.ActiveItemId;
            }

            if (selection.ActiveOrdinal <= 0) {
                return string.Empty;
            }

            int optionIndex = selection.ActiveOrdinal - 1;
            return optionIndex >= 0 && optionIndex < lastPublishedInterpretationOptionIds.Length
                ? lastPublishedInterpretationOptionIds[optionIndex]
                : string.Empty;
        }

        private sealed record CompletionRenderState(
            bool IsPaged,
            int TotalItemCount,
            int WindowOffset,
            string SelectedItemId,
            string[] ItemIds);

        private sealed class PromptMultilineDisplayHighlightCache(PromptSurfaceCompiler compiler)
        {
            private readonly PromptSurfaceCompiler compiler = compiler;
            private string bufferText = string.Empty;
            private Dictionary<int, PromptHighlightSpan[]> highlightsByLine = [];

            public PromptHighlightSpan[] Resolve(
                PromptInputState authorityReactiveState,
                PromptLogicalLineProjection currentProjection,
                IReadOnlyList<PromptHighlightSpan> currentLineHighlights,
                bool refreshAll) {
                var nextBufferText = authorityReactiveState.InputText ?? string.Empty;
                if (refreshAll || !string.Equals(bufferText, nextBufferText, StringComparison.Ordinal)) {
                    bufferText = nextBufferText;
                    highlightsByLine = BuildHighlightsByLine(authorityReactiveState, currentProjection);
                }

                if (currentLineHighlights.Count > 0) {
                    // When the caret leaves a line without changing text, keep the exact current-line
                    // highlight result the operator last saw instead of recomputing that line through
                    // the display-only path.
                    highlightsByLine[currentProjection.LineStartIndex] = [.. currentLineHighlights];
                }
                else {
                    highlightsByLine.Remove(currentProjection.LineStartIndex);
                }

                return [.. highlightsByLine
                    .Where(entry => entry.Key != currentProjection.LineStartIndex)
                    .SelectMany(static entry => entry.Value)
                    .OrderBy(static span => span.StartIndex)
                    .ThenBy(static span => span.Length)];
            }

            private Dictionary<int, PromptHighlightSpan[]> BuildHighlightsByLine(
                PromptInputState authorityReactiveState,
                PromptLogicalLineProjection currentProjection) {
                var results = new Dictionary<int, PromptHighlightSpan[]>();
                foreach (var projection in EnumerateLogicalLines(authorityReactiveState.InputText)) {
                    if (projection.LineStartIndex == currentProjection.LineStartIndex) {
                        continue;
                    }

                    var highlights = ComputeLineHighlights(authorityReactiveState, projection);
                    if (highlights.Length > 0) {
                        results[projection.LineStartIndex] = highlights;
                    }
                }

                return results;
            }

            private PromptHighlightSpan[] ComputeLineHighlights(
                PromptInputState authorityReactiveState,
                PromptLogicalLineProjection projection) {
                var computation = compiler.BuildReactive(projection.Project(new PromptInputState {
                    InputText = authorityReactiveState.InputText,
                    CursorIndex = projection.LineContentEndIndex,
                    PreferredInterpretationId = authorityReactiveState.PreferredInterpretationId,
                })).Computation;
                return projection.RebaseInputHighlights(computation.InputHighlights);
            }

            private static IEnumerable<PromptLogicalLineProjection> EnumerateLogicalLines(string? text) {
                var bufferText = text ?? string.Empty;
                if (bufferText.Length == 0) {
                    yield return PromptLogicalLineProjection.Create(bufferText, 0);
                    yield break;
                }

                var lineStartIndex = 0;
                while (true) {
                    var projection = PromptLogicalLineProjection.Create(bufferText, lineStartIndex);
                    yield return projection;
                    if (projection.LineContentEndIndex >= bufferText.Length) {
                        yield break;
                    }

                    lineStartIndex = LogicalLineBounds.GetNextLineStartIndex(
                        bufferText,
                        projection.LineContentEndIndex);
                }
            }
        }
    }
