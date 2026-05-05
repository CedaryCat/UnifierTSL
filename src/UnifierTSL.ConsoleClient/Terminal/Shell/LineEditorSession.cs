using System.Text;
using UnifierTSL.Contracts.Display;
using UnifierTSL.Contracts.Projection;
using UnifierTSL.Contracts.Sessions;
using UnifierTSL.Contracts.Terminal;
using UnifierTSL.Contracts.Terminal.Overlay;
using UnifierTSL.TextEditing;
using UnifierTSL.Terminal;
using UnifierTSL.Terminal.Runtime;

namespace UnifierTSL.Terminal.Shell
{
    public enum EditorActionKind
    {
        None,
        Redraw,
        Submit,
        Autocomplete,
        ScrollStatus,
        SelectActivity,
    }

    public readonly record struct LineEditorInputAction(
        EditorActionKind Kind,
        string? Payload = null,
        int Delta = 0,
        bool ForceRawSubmit = false)
    {
        public static readonly LineEditorInputAction None = new(EditorActionKind.None);
        public static readonly LineEditorInputAction Redraw = new(EditorActionKind.Redraw);
        public static readonly LineEditorInputAction Autocomplete = new(EditorActionKind.Autocomplete);
        public static LineEditorInputAction Submit(string line, bool forceRawSubmit = false) => new(EditorActionKind.Submit, line, 0, forceRawSubmit);
        public static LineEditorInputAction ScrollStatus(int delta) => new(EditorActionKind.ScrollStatus, null, delta);
        public static LineEditorInputAction SelectActivity(int delta) => new(EditorActionKind.SelectActivity, null, delta);
    }

    internal readonly record struct LineEditorRenderState(
        string Text,
        int CursorIndex,
        int CompletionIndex,
        int CompletionCount,
        ClientBufferedTextMarker[] Markers,
        ProjectionMarkerCatalogItem[] MarkerCatalog,
        EditorPaneKind Kind,
        TerminalOverlay Overlay,
        TerminalOverlayLayoutPlan OverlayPlan,
        bool InterpretationDismissed);

    public sealed class LineEditorSession
    {
        private const int MaxHistoryItems = 200;

        private readonly StringBuilder buffer = new();
        private readonly EditorHistory singleLineHistory = new();
        private readonly EditorHistory multiLineHistory = new();

        private int cursorIndex;
        private long clientBufferRevision;
        private long acceptedRemoteRevision;
        private bool completionActive;
        private int completionIndex = -1;
        private bool pagingActive;
        private int pagedCandidateCount;
        private int pageWindowOffset;
        private int requestedPageOffset;
        private int globalCompletionIndex;
        // Only explicit completion navigation is retained across reactive updates.
        private CompletionSelectionSignature pinnedCompletionSignature;
        private string preferredPresentationText = string.Empty;
        private string preferredInterpretationId = string.Empty;
        private bool ctrlEnterBypassesPreview = true;
        private TerminalSurfaceRuntimeFrame currentFrame = TerminalSurfaceRuntimeFrame.Empty;
        private EditorPaneKind editorKind = EditorPaneKind.SingleLine;
        private TerminalOverlay overlay = TerminalOverlay.Default;
        private TerminalOverlayLayoutPlan overlayPlan = TerminalOverlayLayoutPlan.Empty;
        private EditorKeymap keymap = new();
        private EditorAuthoringBehavior authoringBehavior = new();
        private ClientBufferedTextMarker[] markers = [];
        private ProjectionMarkerCatalogItem[] markerCatalog = [];
        private bool interpretationDismissed;
        private bool completionDismissed;
        private CompletionOpenRequest pendingCompletionOpenRequest;

        private enum CompletionOpenRequest : byte
        {
            None,
            Manual,
            Automatic,
        }

        private enum ExternalFrameApplyResult : byte
        {
            Rejected,
            MetadataOnly,
            BufferAccepted,
        }

        private readonly record struct FrameContext(
            EditorPaneKind EditorKind,
            TerminalOverlay Overlay,
            TerminalOverlayLayoutPlan OverlayPlan,
            EditorKeymap Keymap,
            EditorAuthoringBehavior AuthoringBehavior);

        private readonly record struct CompletionSelectionSignature(
            string Id,
            string Label,
            string SecondaryLabel,
            string TrailingLabel,
            string EditText)
        {
            public bool HasValue =>
                !string.IsNullOrWhiteSpace(Id)
                || !string.IsNullOrWhiteSpace(Label)
                || !string.IsNullOrWhiteSpace(SecondaryLabel)
                || !string.IsNullOrWhiteSpace(TrailingLabel)
                || !string.IsNullOrWhiteSpace(EditText);

            public bool Matches(ProjectionCollectionItem item) {
                if (!string.IsNullOrWhiteSpace(Id) && !string.IsNullOrWhiteSpace(item.ItemId)) {
                    return string.Equals(item.ItemId, Id, StringComparison.Ordinal);
                }

                return string.Equals(ProjectionStyledTextAdapter.ToInlineSegments(item.Label).Text ?? string.Empty, Label ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(ProjectionStyledTextAdapter.ToInlineSegments(item.SecondaryLabel).Text ?? string.Empty, SecondaryLabel ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(ProjectionStyledTextAdapter.ToInlineSegments(item.TrailingLabel).Text ?? string.Empty, TrailingLabel ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(item.PrimaryEdit.NewText ?? string.Empty, EditText ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            }

            public static CompletionSelectionSignature Create(ProjectionCollectionItem item) {
                return new(
                    item.ItemId ?? string.Empty,
                    ProjectionStyledTextAdapter.ToInlineSegments(item.Label).Text ?? string.Empty,
                    ProjectionStyledTextAdapter.ToInlineSegments(item.SecondaryLabel).Text ?? string.Empty,
                    ProjectionStyledTextAdapter.ToInlineSegments(item.TrailingLabel).Text ?? string.Empty,
                    item.PrimaryEdit.NewText ?? string.Empty);
            }
        }

        private sealed class EditorHistory
        {
            public List<string> Entries { get; } = [];
            public int Index { get; set; } = -1;
            public string Draft { get; set; } = string.Empty;
        }

        public LineEditorSession() {
        }

        internal void BeginSession(
            TerminalSurfaceRuntimeFrame? frame) {
            frame ??= TerminalSurfaceRuntimeFrame.Empty;
            var context = ResolveFrameContext(frame);
            ApplyFrameContext(context);
            currentFrame = frame;
            clientBufferRevision = Math.Max(0, frame.EditorPane.ExpectedClientBufferRevision);
            acceptedRemoteRevision = Math.Max(0, frame.EditorPane.RemoteRevision);
            buffer.Clear();
            if (!string.IsNullOrEmpty(frame.EditorPane.BufferText)) {
                buffer.Append(frame.EditorPane.BufferText);
            }

            markerCatalog = [.. (frame.EditorPane.MarkerCatalog ?? [])];
            markers = EditorTextMarkerOps.Normalize(frame.EditorPane.Markers, buffer.Length);
            cursorIndex = EditorTextMarkerOps.SnapCaretToBoundary(markers, frame.EditorPane.CaretIndex, buffer.Length);
            preferredPresentationText = string.Empty;
            preferredInterpretationId = string.Empty;
            ctrlEnterBypassesPreview = true;
            interpretationDismissed = false;
            completionDismissed = false;
            pendingCompletionOpenRequest = CompletionOpenRequest.None;
            ResetPagingState();
            ResetCompletion();
            ResetHistoryNavigation();
            UpdateFrameState(frame, context);
        }

        internal bool ApplyExternalFrame(
            TerminalSurfaceRuntimeFrame? frame) {
            frame ??= TerminalSurfaceRuntimeFrame.Empty;
            var context = ResolveFrameContext(frame);
            var applyResult = ApplyRemoteBufferProposal(frame.EditorPane);
            if (applyResult == ExternalFrameApplyResult.Rejected) {
                return false;
            }

            if (applyResult == ExternalFrameApplyResult.BufferAccepted) {
                preferredPresentationText = string.Empty;
                preferredInterpretationId = string.Empty;
                interpretationDismissed = false;
                completionDismissed = false;
                ResetCompletion();
                ResetHistoryNavigation();
            }

            UpdateFrameState(frame, context);
            return true;
        }

        private ExternalFrameApplyResult ApplyRemoteBufferProposal(EditorPaneRuntimeState editorPane) {
            if (editorPane.Authority != EditorAuthority.ClientBuffered) {
                string authoritativeText = editorPane.BufferText ?? string.Empty;
                ReplaceText(
                    authoritativeText,
                    editorPane.CaretIndex,
                    EditorTextMarkerOps.Normalize(editorPane.Markers, authoritativeText.Length),
                    [.. (editorPane.MarkerCatalog ?? [])]);
                return ExternalFrameApplyResult.BufferAccepted;
            }

            var remoteRevision = Math.Max(0, editorPane.RemoteRevision);
            if (remoteRevision <= acceptedRemoteRevision) {
                var frameText = editorPane.BufferText ?? string.Empty;
                if (!string.Equals(frameText, buffer.ToString(), StringComparison.Ordinal)) {
                    return ExternalFrameApplyResult.Rejected;
                }

                var frameMarkers = EditorTextMarkerOps.Normalize(editorPane.Markers, frameText.Length);
                ProjectionMarkerCatalogItem[] frameCatalog = [.. (editorPane.MarkerCatalog ?? [])];
                if (Math.Max(0, editorPane.ExpectedClientBufferRevision) == clientBufferRevision
                    && (!EditorTextMarkerOps.ContentEquals(markers, frameMarkers)
                        || !HasSameMarkerCatalog(frameCatalog))) {
                    ReplaceText(frameText, cursorIndex, frameMarkers, frameCatalog, advanceClientRevision: false);
                }

                return ExternalFrameApplyResult.MetadataOnly;
            }

            if (Math.Max(0, editorPane.ExpectedClientBufferRevision) != clientBufferRevision) {
                return ExternalFrameApplyResult.Rejected;
            }

            string bufferText = editorPane.BufferText ?? string.Empty;
            var normalizedMarkers = EditorTextMarkerOps.Normalize(editorPane.Markers, bufferText.Length);
            ProjectionMarkerCatalogItem[] normalizedCatalog = [.. (editorPane.MarkerCatalog ?? [])];
            int normalizedCaretIndex = EditorTextMarkerOps.SnapCaretToBoundary(normalizedMarkers, editorPane.CaretIndex, bufferText.Length);
            ReplaceText(bufferText, normalizedCaretIndex, normalizedMarkers, normalizedCatalog, advanceClientRevision: false);
            acceptedRemoteRevision = remoteRevision;
            return ExternalFrameApplyResult.BufferAccepted;
        }

        internal void UpdateSurfaceFrame(
            TerminalSurfaceRuntimeFrame? frame) {
            frame ??= TerminalSurfaceRuntimeFrame.Empty;
            UpdateFrameState(frame, ResolveFrameContext(frame));
        }

        private void UpdateFrameState(TerminalSurfaceRuntimeFrame? frame, in FrameContext context) {
            var nextFrame = frame ?? TerminalSurfaceRuntimeFrame.Empty;
            string currentText = buffer.ToString();
            if (!HasSynchronizedEditorContent(nextFrame, currentText)) {
                if (ShouldPreserveUnsynchronizedPopupAssist()) {
                    return;
                }

                ApplyFrameContext(context);
                ApplyFrameState(nextFrame);
                preferredPresentationText = string.Empty;
                ResetCompletion(clearPendingRequest: false);
                return;
            }

            string previousActiveOptionId = ResolveActiveOptionId(currentFrame);
            ApplyFrameContext(context);
            ApplyFrameState(nextFrame);
            bool clearedPreference = EnsurePreferredOptionIsValid();
            string nextActiveOptionId = ResolveActiveOptionId(currentFrame);
            if (clearedPreference || !string.Equals(previousActiveOptionId, nextActiveOptionId, StringComparison.Ordinal)) {
                preferredPresentationText = string.Empty;
                if (!completionActive) {
                    ResetCompletion(clearPendingRequest: false);
                }
            }

            SyncCompletionWindow(currentText);
        }

        public void RefreshPreviewPreference() {
            preferredPresentationText = ResolveVisibleCompletionText(buffer.ToString());
        }

        private void ApplyFrameState(TerminalSurfaceRuntimeFrame frame) {
            currentFrame = frame;
            ctrlEnterBypassesPreview = ResolveSubmitBehavior().CtrlEnterBypassesPreview;
        }

        private FrameContext ResolveFrameContext(TerminalSurfaceRuntimeFrame frame) {
            var resolvedOverlay = frame.Overlay ?? TerminalOverlay.Default;
            return new FrameContext(
                frame.EditorPane.Kind,
                resolvedOverlay,
                TerminalOverlayLayoutPlan.Create(resolvedOverlay),
                frame.EditorPane.Keymap ?? new EditorKeymap(),
                frame.EditorPane.AuthoringBehavior ?? new EditorAuthoringBehavior());
        }

        private void ApplyFrameContext(in FrameContext context) {
            editorKind = context.EditorKind;
            overlay = context.Overlay;
            overlayPlan = context.OverlayPlan;
            keymap = context.Keymap;
            authoringBehavior = context.AuthoringBehavior;
        }

        internal LineEditorRenderState GetRenderState() {
            string text = buffer.ToString();
            int completionSelectionCount = completionActive ? GetCompletionSelectionCount() : 0;
            return new LineEditorRenderState(
                Text: text,
                CursorIndex: cursorIndex,
                CompletionIndex: completionSelectionCount > 0 ? GetSelectedCompletionOrdinal() : 0,
                CompletionCount: completionSelectionCount,
                Markers: [.. markers],
                MarkerCatalog: [.. markerCatalog],
                Kind: editorKind,
                Overlay: overlay,
                OverlayPlan: overlayPlan,
                InterpretationDismissed: interpretationDismissed);
        }

        public ClientBufferedEditorState BuildBufferedState() {
            string text = buffer.ToString();
            List<ClientBufferedEditorSelection> selections = [];
            List<ClientBufferedEditorCollection> collections = [];
            if (!string.IsNullOrWhiteSpace(preferredPresentationText)) {
                selections.Add(new ClientBufferedEditorSelection {
                    SemanticKey = EditorProjectionSemanticKeys.InputGhost,
                    ActiveItemId = preferredPresentationText,
                    SelectedItemIds = [preferredPresentationText],
                });
            }

            var completionOrdinal = completionActive ? GetSelectedCompletionOrdinal() : 0;
            var selectedCompletion = completionActive ? ResolveSelectedCompletionItem(text) : null;
            bool publishPinnedSelection = ShouldPublishPinnedCompletionSelection(selectedCompletion);
            bool publishPagedOrdinal = pagingActive && completionActive && completionOrdinal > 0;
            if ((publishPinnedSelection && (completionOrdinal > 0 || !string.IsNullOrWhiteSpace(selectedCompletion?.ItemId)))
                || publishPagedOrdinal) {
                selections.Add(new ClientBufferedEditorSelection {
                    SemanticKey = EditorProjectionSemanticKeys.AssistPrimaryList,
                    ActiveItemId = publishPinnedSelection ? selectedCompletion?.ItemId ?? string.Empty : string.Empty,
                    ActiveOrdinal = completionOrdinal,
                    SelectedItemIds = publishPinnedSelection && !string.IsNullOrWhiteSpace(selectedCompletion?.ItemId)
                        ? [selectedCompletion.ItemId]
                        : [],
                });
            }

            var interpretationId = ResolveBufferedInterpretationId();
            var interpretationOrdinal = ResolveBufferedInterpretationOrdinal();
            if (interpretationOrdinal > 0 || !string.IsNullOrWhiteSpace(interpretationId)) {
                selections.Add(new ClientBufferedEditorSelection {
                    SemanticKey = EditorProjectionSemanticKeys.AssistSecondaryList,
                    ActiveOrdinal = interpretationOrdinal,
                    ActiveItemId = interpretationId,
                    SelectedItemIds = string.IsNullOrWhiteSpace(interpretationId) ? [] : [interpretationId],
                });
            }

            var completion = TerminalProjectionAssistAdapter.FindCompletion(currentFrame);
            var totalCompletionCount = pagingActive
                ? Math.Max(0, pagedCandidateCount)
                : Math.Max(completion?.State.TotalItemCount ?? 0, completion?.State.Items?.Length ?? 0);
            var completionWindowOffset = pagingActive && completionActive
                ? requestedPageOffset
                : Math.Max(0, completion?.State.WindowOffset ?? 0);
            var completionPageSize = Math.Max(0, completion?.State.PageSize ?? 0);
            if (totalCompletionCount > 0 || completionWindowOffset > 0 || completionPageSize > 0) {
                collections.Add(new ClientBufferedEditorCollection {
                    SemanticKey = EditorProjectionSemanticKeys.AssistPrimaryList,
                    TotalItemCount = totalCompletionCount,
                    WindowOffset = completionWindowOffset,
                    PageSize = completionPageSize,
                });
            }

            return new ClientBufferedEditorState {
                Kind = editorKind,
                BufferText = text,
                CaretIndex = cursorIndex,
                ClientBufferRevision = clientBufferRevision,
                AcceptedRemoteRevision = acceptedRemoteRevision,
                Markers = [.. markers.Select(static marker => new ClientBufferedTextMarker {
                    Key = marker.Key,
                    VariantKey = marker.VariantKey,
                    StartIndex = marker.StartIndex,
                    Length = marker.Length,
                })],
                Selections = [.. selections],
                Collections = [.. collections],
            };
        }

        public LineEditorInputAction ApplyKey(ConsoleKeyInfo key) {
            return keymap.DispatchPolicy switch {
                EditorKeyDispatchPolicy.Standard => ApplyStandardKey(key),
                _ => LineEditorInputAction.None,
            };
        }

        private LineEditorInputAction ApplyStandardKey(ConsoleKeyInfo key) {
            if (MatchesAny(keymap.AltSubmit, key)) {
                return SubmitCurrentBuffer(forceRawSubmit: ctrlEnterBypassesPreview);
            }

            if (MatchesAny(keymap.Submit, key)) {
                return HandleSubmitChord();
            }

            if (MatchesAny(keymap.NewLine, key)) {
                if (editorKind != EditorPaneKind.MultiLine) {
                    return LineEditorInputAction.None;
                }

                return InsertText("\n");
            }

            if (!completionActive) {
                if (MatchesAny(keymap.PrevActivity, key)) {
                    return LineEditorInputAction.SelectActivity(-1);
                }

                if (MatchesAny(keymap.NextActivity, key)) {
                    return LineEditorInputAction.SelectActivity(1);
                }
            }

            bool canInteractWithCompletion = completionActive && GetCompletionSelectionCount() > 0;
            if (MatchesAny(keymap.AcceptCompletion, key) && canInteractWithCompletion) {
                return AcceptSelectedCompletion();
            }

            if (MatchesAny(keymap.PrevCompletion, key) && canInteractWithCompletion) {
                return MoveCompletionSelection(-1);
            }

            if (MatchesAny(keymap.NextCompletion, key) && canInteractWithCompletion) {
                return MoveCompletionSelection(1);
            }

            bool interpretationUiAvailable = !completionActive
                && !interpretationDismissed
                && HasMultipleInterpretationOptions()
                && overlayPlan.InterpretationAssistControl is not null;
            if (MatchesAny(keymap.PrevInterpretation, key) && interpretationUiAvailable && overlayPlan.SupportsPrevInterpretation) {
                return MoveInterpretationHint(-1);
            }

            if (MatchesAny(keymap.NextInterpretation, key) && interpretationUiAvailable && overlayPlan.SupportsNextInterpretation) {
                return MoveInterpretationHint(1);
            }

            if (MatchesAny(keymap.DismissAssist, key) && (completionActive || HasInterpretationHint())) {
                if (!overlayPlan.UsesPopupAssist) {
                    return completionActive
                        ? CancelCompletion()
                        : LineEditorInputAction.None;
                }

                return HandleEscape();
            }

            if (MatchesAny(keymap.AcceptPreview, key)
                && overlayPlan.SupportsAcceptPreview
                && TryAcceptPreview()) {
                return LineEditorInputAction.Redraw;
            }

            if (MatchesAny(keymap.ScrollStatusUp, key) && overlayPlan.SupportsStatusScrollUp) {
                return LineEditorInputAction.ScrollStatus(-1);
            }

            if (MatchesAny(keymap.ScrollStatusDown, key) && overlayPlan.SupportsStatusScrollDown) {
                return LineEditorInputAction.ScrollStatus(1);
            }

            if (MatchesAny(keymap.ManualCompletion, key)) {
                if (!overlayPlan.UsesPopupCompletion && !IsCursorAtLogicalLineEnd()) {
                    return LineEditorInputAction.None;
                }

                pendingCompletionOpenRequest = CompletionOpenRequest.Manual;
                completionDismissed = false;
                interpretationDismissed = false;
                return TryOpenCompletionNow(buffer.ToString())
                    ? LineEditorInputAction.Autocomplete
                    : LineEditorInputAction.None;
            }

            return HandleEditorKey(key);
        }

        private LineEditorInputAction HandleEditorKey(ConsoleKeyInfo key) {
            bool hasShift = (key.Modifiers & ConsoleModifiers.Shift) != 0;
            bool hasControl = (key.Modifiers & ConsoleModifiers.Control) != 0;
            bool hasAlt = (key.Modifiers & ConsoleModifiers.Alt) != 0;
            if (hasControl) {
                return HandleControlKey(key);
            }

            if (hasAlt) {
                return LineEditorInputAction.None;
            }

            return key.Key switch {
                ConsoleKey.Backspace => HandleBackspace(),
                ConsoleKey.Delete => HandleDelete(),
                ConsoleKey.LeftArrow => MoveCursor(-1),
                ConsoleKey.RightArrow => MoveCursor(1),
                ConsoleKey.Home => editorKind == EditorPaneKind.MultiLine ? MoveCursorToLineBoundary(true) : MoveCursorTo(0),
                ConsoleKey.End => editorKind == EditorPaneKind.MultiLine ? MoveCursorToLineBoundary(false) : MoveCursorTo(buffer.Length),
                ConsoleKey.UpArrow => HandleVerticalNavigation(-1),
                ConsoleKey.DownArrow => HandleVerticalNavigation(1),
                ConsoleKey.Tab when hasShift => LineEditorInputAction.None,
                ConsoleKey.Tab => LineEditorInputAction.None,
                ConsoleKey.Escape => LineEditorInputAction.None,
                _ => HandlePrintableKey(key),
            };
        }

        private LineEditorInputAction HandleControlKey(ConsoleKeyInfo key) {
            return key.Key switch {
                ConsoleKey.A => MoveCursorTo(0),
                ConsoleKey.E => MoveCursorTo(buffer.Length),
                ConsoleKey.U => ClearLine(),
                _ => LineEditorInputAction.None,
            };
        }

        private bool TryOpenCompletionNow(string currentText) {
            IReadOnlyList<ProjectionCollectionItem> items = ResolveCompletionItems(currentText);
            if (items.Count == 0) {
                return false;
            }

            completionActive = true;
            int preferred = ResolvePreferredSelectionOrdinal(items.Count, currentText);
            SetSelectedCompletionOrdinal(preferred <= 0 ? 1 : preferred);
            pendingCompletionOpenRequest = CompletionOpenRequest.None;
            return true;
        }

        private bool TryAcceptPreview() {
            string currentText = buffer.ToString();
            IReadOnlyList<ProjectionCollectionItem> items = ResolveCompletionItems(currentText);
            if (items.Count > 0) {
                ProjectionCollectionItem? item = ResolveSelectedCompletionItem(currentText);
                if (item is null) {
                    int previewOrdinal = ResolvePreviewSelectionOrdinal(items, currentText);
                    if (previewOrdinal > 0) {
                        int localIndex = previewOrdinal - 1;
                        if (localIndex >= 0 && localIndex < items.Count) {
                            item = items[localIndex];
                        }
                    }
                }

                if (item is not null) {
                    string completionValue = TerminalProjectionAssistAdapter.ResolvePrimaryEdit(currentFrame, item)
                        .Apply(currentText, out int completionCaretIndex);
                    if (!string.Equals(completionValue, currentText, StringComparison.Ordinal)) {
                        ReplaceText(completionValue, completionCaretIndex);
                        preferredPresentationText = string.Empty;
                        ResetCompletion();
                        ResetHistoryNavigation();
                        return true;
                    }
                }
            }

            GhostInlineHint ghostHint = ResolveGhostHint();
            if (!GhostInlineHintOps.TryApply(currentText, ghostHint, out string previewValue, out int previewCaretIndex)) {
                return false;
            }

            return AcceptGhostText(previewValue, previewCaretIndex);
        }

        private LineEditorInputAction HandleSubmitChord() {
            if (editorKind != EditorPaneKind.MultiLine
                || authoringBehavior.MultilineSubmitMode != MultilineSubmitMode.UseReadiness) {
                return SubmitCurrentBuffer();
            }

            return ShouldSubmitMultilineBuffer()
                ? SubmitCurrentBuffer()
                : InsertText("\n");
        }

        private LineEditorInputAction AcceptSelectedCompletion() {
            string currentText = buffer.ToString();
            ProjectionCollectionItem? item = ResolveSelectedCompletionItem(currentText);
            if (item is null) {
                ResetCompletion();
                return LineEditorInputAction.Redraw;
            }

            string completionValue = TerminalProjectionAssistAdapter.ResolvePrimaryEdit(currentFrame, item)
                .Apply(currentText, out int caret);
            ReplaceText(completionValue, caret);
            preferredPresentationText = string.Empty;
            interpretationDismissed = false;
            completionDismissed = true;
            ResetCompletion();
            ResetHistoryNavigation();
            return LineEditorInputAction.Autocomplete;
        }

        private LineEditorInputAction MoveCompletionSelection(int delta) {
            int count = GetCompletionSelectionCount();
            if (!completionActive || count <= 0) {
                return LineEditorInputAction.None;
            }

            int current = GetSelectedCompletionOrdinal();
            int next = current <= 0
                ? 1
                : delta < 0
                    ? (current - 2 + count) % count + 1
                    : current % count + 1;
            SetSelectedCompletionOrdinal(next, pinSelection: true);
            return LineEditorInputAction.Autocomplete;
        }

        private LineEditorInputAction HandleEscape() {
            bool changed = false;
            if (completionActive) {
                ResetCompletion();
                completionDismissed = true;
                changed = true;
            }

            if (!interpretationDismissed && HasInterpretationHint()) {
                interpretationDismissed = true;
                changed = true;
            }

            return changed ? LineEditorInputAction.Redraw : LineEditorInputAction.None;
        }

        private LineEditorInputAction CancelCompletion() {
            if (!completionActive) {
                return LineEditorInputAction.None;
            }

            ResetCompletion();
            return LineEditorInputAction.Redraw;
        }

        private LineEditorInputAction HandleBackspace() {
            if (cursorIndex == 0 || buffer.Length == 0) {
                return LineEditorInputAction.None;
            }

            if (EditorTextMarkerOps.TryFindMarkerBeforeCursor(markers, cursorIndex, out var marker)) {
                RemoveMarker(marker, keepCursorAtStart: true);
                return LineEditorInputAction.Redraw;
            }

            buffer.Remove(cursorIndex - 1, 1);
            cursorIndex -= 1;
            markers = EditorTextMarkerOps.ApplyTextChange(markers, cursorIndex, 1, 0, buffer.Length);
            OnBufferMutated(preservePopupAssistCompletion: true);
            return LineEditorInputAction.Redraw;
        }

        private LineEditorInputAction HandleDelete() {
            if (cursorIndex >= buffer.Length) {
                return LineEditorInputAction.None;
            }

            if (EditorTextMarkerOps.TryFindMarkerAtCursor(markers, cursorIndex, out var marker)) {
                RemoveMarker(marker, keepCursorAtStart: true);
                return LineEditorInputAction.Redraw;
            }

            buffer.Remove(cursorIndex, 1);
            markers = EditorTextMarkerOps.ApplyTextChange(markers, cursorIndex, 1, 0, buffer.Length);
            OnBufferMutated(preservePopupAssistCompletion: true);
            return LineEditorInputAction.Redraw;
        }

        private LineEditorInputAction HandlePrintableKey(ConsoleKeyInfo key) {
            if (char.IsControl(key.KeyChar) || (key.Modifiers & ConsoleModifiers.Alt) != 0) {
                return LineEditorInputAction.None;
            }

            if (TryMaterializeLeadingMarker(key.KeyChar)) {
                return LineEditorInputAction.Redraw;
            }

            LineEditorInputAction action = InsertText(key.KeyChar.ToString(), preservePopupAssistCompletion: true);
            if (overlayPlan.UsesPopupCompletion) {
                RegisterAutoCompletionTrigger(key);
            }

            return action;
        }

        private void RegisterAutoCompletionTrigger(ConsoleKeyInfo key) {
            if (!authoringBehavior.OpensCompletionAutomatically) {
                pendingCompletionOpenRequest = CompletionOpenRequest.None;
                return;
            }

            if ((key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) != 0) {
                pendingCompletionOpenRequest = CompletionOpenRequest.None;
                return;
            }

            char ch = key.KeyChar;
            if (ch == '\0') {
                pendingCompletionOpenRequest = CompletionOpenRequest.None;
                return;
            }

            pendingCompletionOpenRequest = CompletionOpenRequest.Automatic;
            completionDismissed = false;
        }

        private LineEditorInputAction InsertText(string text, bool preservePopupAssistCompletion = false) {
            if (string.IsNullOrEmpty(text)) {
                return LineEditorInputAction.None;
            }

            buffer.Insert(cursorIndex, text);
            cursorIndex += text.Length;
            markers = EditorTextMarkerOps.ApplyTextChange(markers, cursorIndex - text.Length, 0, text.Length, buffer.Length);
            OnBufferMutated(preservePopupAssistCompletion);
            return LineEditorInputAction.Redraw;
        }

        private void OnBufferMutated(bool preservePopupAssistCompletion = false) {
            AdvanceClientBufferRevision();
            interpretationDismissed = false;
            completionDismissed = false;
            bool keepCompletion = preservePopupAssistCompletion
                && overlayPlan.UsesPopupCompletion
                && completionActive
                && GetCompletionSelectionCount() > 0;
            if (!keepCompletion) {
                ResetCompletion();
            }
            ResetHistoryNavigation();
        }

        private LineEditorInputAction HandleVerticalNavigation(int delta) {
            if (editorKind == EditorPaneKind.MultiLine) {
                return MoveVertical(delta);
            }

            return delta < 0 ? HandleHistoryUp() : HandleHistoryDown();
        }

        private LineEditorInputAction MoveCursor(int delta) {
            int target = delta switch {
                > 0 when EditorTextMarkerOps.TryFindMarkerAtCursor(markers, cursorIndex, out var marker) => marker.StartIndex + marker.Length,
                < 0 when EditorTextMarkerOps.TryFindMarkerBeforeCursor(markers, cursorIndex, out var marker) => marker.StartIndex,
                _ => Math.Clamp(cursorIndex + delta, 0, buffer.Length),
            };
            if (target == cursorIndex) {
                return LineEditorInputAction.None;
            }

            cursorIndex = target;
            ResetCompletion();
            return LineEditorInputAction.Redraw;
        }

        private LineEditorInputAction MoveCursorTo(int target) {
            int bounded = EditorTextMarkerOps.SnapCaretToBoundary(markers, target, buffer.Length);
            if (bounded == cursorIndex) {
                return LineEditorInputAction.None;
            }

            cursorIndex = bounded;
            ResetCompletion();
            return LineEditorInputAction.Redraw;
        }

        private LineEditorInputAction MoveCursorToLineBoundary(bool toStart) {
            if (editorKind != EditorPaneKind.MultiLine) {
                return MoveCursorTo(toStart ? 0 : buffer.Length);
            }

            string text = buffer.ToString();
            int target = toStart
                ? LogicalLineBounds.GetStartIndex(text, cursorIndex)
                : LogicalLineBounds.GetContentEndIndex(text, cursorIndex);
            return MoveCursorTo(target);
        }

        private LineEditorInputAction MoveVertical(int delta) {
            if (editorKind != EditorPaneKind.MultiLine || delta == 0) {
                return LineEditorInputAction.None;
            }

            string text = buffer.ToString();
            int lineStart = LogicalLineBounds.GetStartIndex(text, cursorIndex);
            int lineEnd = LogicalLineBounds.GetContentEndIndex(text, lineStart);
            int column = cursorIndex - lineStart;
            if (delta < 0) {
                if (lineStart == 0) {
                    return LineEditorInputAction.None;
                }

                int prevStart = LogicalLineBounds.GetPreviousLineStartIndex(text, lineStart);
                int prevEnd = LogicalLineBounds.GetContentEndIndex(text, prevStart);
                return MoveCursorTo(Math.Min(prevStart + column, prevEnd));
            }

            if (lineEnd >= text.Length) {
                return LineEditorInputAction.None;
            }

            int nextStart = LogicalLineBounds.GetNextLineStartIndex(text, lineEnd);
            int nextEnd = LogicalLineBounds.GetContentEndIndex(text, nextStart);
            return MoveCursorTo(Math.Min(nextStart + column, nextEnd));
        }

        private bool ShouldSubmitMultilineBuffer() {
            SubmitReadiness readiness = ResolveSubmitBehavior().PlainEnterReadiness;
            if (readiness == SubmitReadiness.Ready) {
                return true;
            }

            if (readiness == SubmitReadiness.NotReady) {
                return false;
            }

            if (cursorIndex < buffer.Length) {
                return false;
            }

            return ShouldSubmitMultilineBufferFallback();
        }

        private bool ShouldSubmitMultilineBufferFallback() {
            string text = buffer.ToString();
            string trimmed = text.TrimEnd();
            if (trimmed.Length == 0) {
                return true;
            }

            int braceDepth = 0;
            bool inString = false;
            bool escape = false;
            foreach (char ch in trimmed) {
                if (inString) {
                    if (escape) {
                        escape = false;
                        continue;
                    }

                    if (ch == '\\') {
                        escape = true;
                        continue;
                    }

                    if (ch == '"') {
                        inString = false;
                    }
                    continue;
                }

                if (ch == '"') {
                    inString = true;
                    continue;
                }

                if (ch == '{') {
                    braceDepth += 1;
                    continue;
                }

                if (ch == '}') {
                    braceDepth = Math.Max(0, braceDepth - 1);
                }
            }

            if (inString || braceDepth > 0) {
                return false;
            }

            char tail = trimmed[^1];
            return tail is not ('{' or '(' or '[' or ',' or '.' or ':');
        }

        private LineEditorInputAction SubmitCurrentBuffer(bool forceRawSubmit = false) {
            string submittedText = ResolveSubmittedText(forceRawSubmit);
            RememberHistory(submittedText);
            preferredInterpretationId = string.Empty;
            ResetCompletion();
            ResetHistoryNavigation();
            return LineEditorInputAction.Submit(submittedText, forceRawSubmit);
        }

        private string ResolveSubmittedText(bool forceRawSubmit) {
            string text = buffer.ToString();
            if (forceRawSubmit
                || ResolveSubmitBehavior().EmptyInputAction != EmptyInputSubmitAction.AcceptPreviewIfAvailable
                || !string.IsNullOrWhiteSpace(text)
                || !GhostInlineHintOps.TryApply(string.Empty, ResolveGhostHint(), out string preview)
                || string.IsNullOrWhiteSpace(preview)) {
                return text;
            }

            ReplaceText(preview);
            preferredPresentationText = string.Empty;
            return preview;
        }

        private void RememberHistory(string text) {
            if (string.IsNullOrWhiteSpace(text)) {
                return;
            }

            EditorHistory history = GetHistory();
            if (history.Entries.Count == 0 || !string.Equals(history.Entries[^1], text, StringComparison.Ordinal)) {
                history.Entries.Add(text);
                if (history.Entries.Count > MaxHistoryItems) {
                    history.Entries.RemoveAt(0);
                }
            }
        }

        private LineEditorInputAction HandleHistoryUp() {
            EditorHistory history = GetHistory();
            if (history.Entries.Count == 0) {
                return LineEditorInputAction.None;
            }

            if (history.Index == -1) {
                history.Draft = buffer.ToString();
                history.Index = history.Entries.Count - 1;
            }
            else if (history.Index > 0) {
                history.Index -= 1;
            }
            else {
                return LineEditorInputAction.None;
            }

            ReplaceText(history.Entries[history.Index]);
            return LineEditorInputAction.Redraw;
        }

        private LineEditorInputAction HandleHistoryDown() {
            EditorHistory history = GetHistory();
            if (history.Entries.Count == 0 || history.Index == -1) {
                return LineEditorInputAction.None;
            }

            if (history.Index < history.Entries.Count - 1) {
                history.Index += 1;
                ReplaceText(history.Entries[history.Index]);
            }
            else {
                history.Index = -1;
                ReplaceText(history.Draft);
            }

            return LineEditorInputAction.Redraw;
        }

        private LineEditorInputAction ClearLine() {
            if (buffer.Length == 0) {
                return LineEditorInputAction.None;
            }

            ReplaceText(string.Empty, 0);
            preferredPresentationText = string.Empty;
            preferredInterpretationId = string.Empty;
            ResetCompletion();
            ResetHistoryNavigation();
            return LineEditorInputAction.Redraw;
        }

        private void SyncCompletionWindow(string currentText) {
            SyncPagingState(TerminalProjectionAssistAdapter.FindCompletion(currentFrame));
            int count = GetCompletionSelectionCount();
            if (count <= 0) {
                completionActive = false;
                completionIndex = -1;
                globalCompletionIndex = 0;
                // Candidate windows can briefly publish empty while providers refresh; the pending
                // automatic request belongs to the current buffer edit and must survive that gap.
                return;
            }

            if (!overlayPlan.UsesPopupCompletion) {
                if (completionActive) {
                    SetSelectedCompletionOrdinal(ResolvePreferredSelectionOrdinal(count, currentText));
                }
                else {
                    completionIndex = -1;
                    globalCompletionIndex = 0;
                    requestedPageOffset = pageWindowOffset;
                }
                return;
            }

            if (completionActive) {
                SetSelectedCompletionOrdinal(ResolvePreferredSelectionOrdinal(count, currentText));
                pendingCompletionOpenRequest = CompletionOpenRequest.None;
                return;
            }

            var openRequest = pendingCompletionOpenRequest;
            bool shouldOpen = openRequest switch {
                CompletionOpenRequest.Manual => true,
                CompletionOpenRequest.Automatic => TerminalProjectionAssistAdapter.ResolveCompletionActivationMode(currentFrame) == CompletionActivationMode.Automatic
                    && !completionDismissed,
                _ => false,
            };
            if (!shouldOpen) {
                if (openRequest == CompletionOpenRequest.Automatic) {
                    pendingCompletionOpenRequest = CompletionOpenRequest.None;
                }

                return;
            }

            if (ResolveCompletionItems(currentText).Count == 0) {
                if (pendingCompletionOpenRequest == CompletionOpenRequest.Automatic) {
                    pendingCompletionOpenRequest = CompletionOpenRequest.None;
                }
                return;
            }

            completionActive = true;
            SetSelectedCompletionOrdinal(ResolvePreferredSelectionOrdinal(count, currentText));
            pendingCompletionOpenRequest = CompletionOpenRequest.None;
        }

        private int ResolvePreferredSelectionOrdinal(int completionCount, string currentText) {
            IReadOnlyList<ProjectionCollectionItem> items = ResolveCompletionItems(currentText);
            var completion = TerminalProjectionAssistAdapter.FindCompletion(currentFrame);
            var completionDetail = TerminalProjectionAssistAdapter.FindCompletionDetail(currentFrame);
            var selectedItemIndex = TerminalProjectionAssistAdapter.ResolveSelectedItemIndex(
                currentFrame,
                completion,
                completionDetail);
            if (pagingActive && selectedItemIndex >= 0) {
                return Math.Clamp(pageWindowOffset + selectedItemIndex + 1, 1, completionCount);
            }

            if (pinnedCompletionSignature.HasValue) {
                for (var index = 0; index < items.Count; index++) {
                    if (!pinnedCompletionSignature.Matches(items[index])) {
                        continue;
                    }

                    return index + 1;
                }
            }

            if (selectedItemIndex >= 0) {
                return pagingActive
                    ? Math.Clamp(pageWindowOffset + selectedItemIndex + 1, 1, completionCount)
                    : Math.Clamp(selectedItemIndex + 1, 1, completionCount);
            }

            int previewOrdinal = ResolvePreviewSelectionOrdinal(items, currentText);
            if (previewOrdinal > 0 && previewOrdinal <= completionCount) {
                return previewOrdinal;
            }

            return 1;
        }

        private int ResolvePreviewSelectionOrdinal(IReadOnlyList<ProjectionCollectionItem> items, string currentText) {
            if (items.Count == 0) {
                return 0;
            }

            string previewCompletionId = ResolveGhostHint().SourceCompletionId ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(previewCompletionId)) {
                for (int index = 0; index < items.Count; index++) {
                    if (string.Equals(items[index].ItemId, previewCompletionId, StringComparison.OrdinalIgnoreCase)) {
                        return index + 1;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(preferredPresentationText)) {
                for (int index = 0; index < items.Count; index++) {
                    if (string.Equals(items[index].ItemId, preferredPresentationText, StringComparison.OrdinalIgnoreCase)) {
                        return index + 1;
                    }
                }
            }

            if (GhostInlineHintOps.TryApply(currentText, ResolveGhostHint(), out string previewText)
                && !string.IsNullOrWhiteSpace(previewText)) {
                for (int index = 0; index < items.Count; index++) {
                    string completionText = TerminalProjectionAssistAdapter.ResolvePrimaryEdit(currentFrame, items[index]).Apply(currentText);
                    if (string.Equals(completionText, previewText, StringComparison.OrdinalIgnoreCase)) {
                        return index + 1;
                    }
                }
            }

            return 0;
        }

        private bool ShouldPublishPinnedCompletionSelection(ProjectionCollectionItem? selectedCompletion) {
            return pinnedCompletionSignature.HasValue
                && selectedCompletion is not null
                && pinnedCompletionSignature.Matches(selectedCompletion);
        }

        private void SyncPagingState(ProjectionCollectionNodeRuntime? completion) {
            if (completion is not { State.IsPaged: true }) {
                ResetPagingState();
                return;
            }

            pagingActive = true;
            pagedCandidateCount = Math.Max(0, completion.Value.State.TotalItemCount);
            pageWindowOffset = Math.Max(0, completion.Value.State.WindowOffset);
            requestedPageOffset = pageWindowOffset;
        }

        private ProjectionCollectionItem? ResolveSelectedCompletionItem(string currentText) {
            IReadOnlyList<ProjectionCollectionItem> items = ResolveCompletionItems(currentText);
            if (items.Count == 0 || completionIndex < 0 || completionIndex >= items.Count) {
                return null;
            }

            return items[completionIndex];
        }

        private IReadOnlyList<ProjectionCollectionItem> ResolveCompletionItems(string currentText) {
            return HasSynchronizedEditorContent(currentText)
                ? TerminalProjectionAssistAdapter.FindCompletion(currentFrame)?.State.Items ?? []
                : [];
        }

        private int GetCompletionSelectionCount() {
            return pagingActive
                ? pagedCandidateCount
                : TerminalProjectionAssistAdapter.FindCompletion(currentFrame)?.State.Items?.Length ?? 0;
        }

        private int GetSelectedCompletionOrdinal() {
            if (!completionActive) {
                return 0;
            }

            return pagingActive
                ? globalCompletionIndex
                : completionIndex >= 0 ? completionIndex + 1 : 0;
        }

        private void SetSelectedCompletionOrdinal(int ordinal, bool pinSelection = false) {
            int count = GetCompletionSelectionCount();
            if (count <= 0) {
                ResetCompletion();
                return;
            }

            int normalized = Math.Clamp(ordinal, 1, count);
            completionActive = true;
            if (pagingActive) {
                globalCompletionIndex = normalized;
            }

            completionIndex = pagingActive
                ? normalized - pageWindowOffset - 1
                : normalized - 1;
            if (pinSelection) {
                pinnedCompletionSignature = ResolveSelectedCompletionSignature();
            }
        }

        private CompletionSelectionSignature ResolveSelectedCompletionSignature() {
            if (completionIndex < 0) {
                return default;
            }

            ProjectionCollectionItem[] items = TerminalProjectionAssistAdapter.FindCompletion(currentFrame)?.State.Items ?? [];
            return completionIndex >= items.Length
                ? default
                : CompletionSelectionSignature.Create(items[completionIndex]);
        }

        private bool EnsurePreferredOptionIsValid() {
            IReadOnlyList<ProjectionCollectionItem> options = ResolveHintOptions();
            if (options.Count <= 1) {
                return ClearPreferredInterpretationId();
            }

            if (string.IsNullOrWhiteSpace(preferredInterpretationId)) {
                return false;
            }

            return options.Any(option => string.Equals(option.ItemId, preferredInterpretationId, StringComparison.Ordinal))
                ? false
                : ClearPreferredInterpretationId();
        }

        private IReadOnlyList<ProjectionCollectionItem> ResolveHintOptions() {
            return [.. (TerminalProjectionAssistAdapter.FindInterpretation(currentFrame)?.State.Items ?? [])
                .Where(static option => !string.IsNullOrWhiteSpace(option.ItemId))];
        }

        private static string ResolveActiveOptionId(TerminalSurfaceRuntimeFrame frame) {
            var interpretation = TerminalProjectionAssistAdapter.FindInterpretation(frame);
            var detail = TerminalProjectionAssistAdapter.FindInterpretationDetail(frame);
            return TerminalProjectionAssistAdapter.ResolveActiveItemId(frame, interpretation, detail);
        }

        private string ResolveBufferedInterpretationId() {
            return preferredInterpretationId ?? string.Empty;
        }

        private int ResolveBufferedInterpretationOrdinal() {
            if (string.IsNullOrWhiteSpace(preferredInterpretationId)) {
                return 0;
            }

            var options = ResolveHintOptions();
            if (options.Count == 0) {
                return 0;
            }

            for (var index = 0; index < options.Count; index++) {
                if (string.Equals(options[index].ItemId, preferredInterpretationId, StringComparison.Ordinal)) {
                    return index + 1;
                }
            }

            return 0;
        }

        private bool ClearPreferredInterpretationId() {
            if (string.IsNullOrWhiteSpace(preferredInterpretationId)) {
                return false;
            }

            preferredInterpretationId = string.Empty;
            return true;
        }

        private bool HasMultipleInterpretationOptions() {
            return ResolveHintOptions().Count > 1;
        }

        private LineEditorInputAction MoveInterpretationHint(int delta) {
            IReadOnlyList<ProjectionCollectionItem> options = ResolveHintOptions();
            if (options.Count <= 1 || delta == 0) {
                return LineEditorInputAction.None;
            }

            int current = TerminalProjectionAssistAdapter.ResolveSelectedItemIndex(
                currentFrame,
                TerminalProjectionAssistAdapter.FindInterpretation(currentFrame),
                TerminalProjectionAssistAdapter.FindInterpretationDetail(currentFrame));
            if (!string.IsNullOrWhiteSpace(preferredInterpretationId)) {
                for (int i = 0; i < options.Count; i++) {
                    if (string.Equals(options[i].ItemId, preferredInterpretationId, StringComparison.Ordinal)) {
                        current = i;
                        break;
                    }
                }
            }

            if (current < 0) {
                current = 0;
            }

            int next = delta < 0
                ? (current - 1 + options.Count) % options.Count
                : (current + 1) % options.Count;
            preferredInterpretationId = options[next].ItemId ?? string.Empty;
            ResetCompletion();
            return LineEditorInputAction.Redraw;
        }

        private bool IsCursorAtLogicalLineEnd() {
            return LogicalLineBounds.IsCursorAtEnd(buffer.ToString(), cursorIndex);
        }

        private bool ShouldPreserveUnsynchronizedPopupAssist() {
            if (!overlayPlan.UsesPopupAssist) {
                return false;
            }

            if (completionActive && GetCompletionSelectionCount() > 0) {
                return true;
            }

            return !interpretationDismissed && HasInterpretationHint();
        }

        private bool HasSynchronizedEditorContent(TerminalSurfaceRuntimeFrame frame, string currentText) {
            return string.Equals(frame.EditorPane.BufferText ?? string.Empty, currentText ?? string.Empty, StringComparison.Ordinal);
        }

        private bool HasSynchronizedEditorContent(string currentText) {
            return HasSynchronizedEditorContent(currentFrame, currentText);
        }

        private string ResolveVisibleCompletionText(string currentText) {
            if (!IsCursorAtLogicalLineEnd() || !HasSynchronizedEditorContent(currentText)) {
                return string.Empty;
            }

            string previewCompletionId = ResolveGhostHint().SourceCompletionId ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(previewCompletionId)) {
                return previewCompletionId;
            }

            return string.Empty;
        }

        private bool HasInterpretationHint() {
            if (InlineSegmentsOps.HasVisibleText(ProjectionStyledTextAdapter.ToInlineSegments(
                TerminalProjectionAssistAdapter.FindInterpretationSummary(currentFrame)?.State.Content))) {
                return true;
            }

            return ProjectionStyledTextAdapter.ToInlineSegments(
                    TerminalProjectionAssistAdapter.FindInterpretationDetail(currentFrame)?.State.Lines)
                .Any(InlineSegmentsOps.HasVisibleText)
                || ResolveHintOptions().Count > 1;
        }

        public bool CapturesRawKeys => authoringBehavior.CapturesRawKeys;

        private EditorSubmitBehavior ResolveSubmitBehavior() {
            return TerminalProjectionInputAdapter.ResolveSubmitBehavior(currentFrame);
        }

        private GhostInlineHint ResolveGhostHint() {
            return TerminalProjectionInputAdapter.ResolveGhostHint(currentFrame);
        }

        private bool AcceptGhostText(string ghostValue, int ghostCaretIndex) {
            string currentText = buffer.ToString();
            if (string.Equals(ghostValue, currentText, StringComparison.Ordinal)) {
                return false;
            }

            ReplaceText(ghostValue, ghostCaretIndex);
            preferredPresentationText = string.Empty;
            ResetCompletion();
            ResetHistoryNavigation();
            return true;
        }

        private bool HasSameAuthoringBehavior(EditorAuthoringBehavior other) {
            return authoringBehavior.OpensCompletionAutomatically == other.OpensCompletionAutomatically
                && authoringBehavior.CapturesRawKeys == other.CapturesRawKeys
                && authoringBehavior.MultilineSubmitMode == other.MultilineSubmitMode;
        }

        private bool MatchesAny(IReadOnlyList<KeyChord>? chords, ConsoleKeyInfo keyInfo) {
            return chords is { Count: > 0 } && chords.Any(chord => chord.Key == keyInfo.Key && chord.Modifiers == keyInfo.Modifiers);
        }

        private void ReplaceText(
            string text,
            int? caretIndex = null,
            IReadOnlyList<ClientBufferedTextMarker>? nextMarkers = null,
            IReadOnlyList<ProjectionMarkerCatalogItem>? nextMarkerCatalog = null,
            bool advanceClientRevision = true) {
            var nextText = text ?? string.Empty;
            var previousText = buffer.ToString();
            buffer.Clear();
            buffer.Append(nextText);
            cursorIndex = Math.Clamp(caretIndex ?? buffer.Length, 0, buffer.Length);
            markerCatalog = nextMarkerCatalog is null ? markerCatalog : [.. nextMarkerCatalog];
            markers = nextMarkers is not null
                ? EditorTextMarkerOps.Normalize(nextMarkers, buffer.Length)
                : TryResolveTextChange(previousText, nextText, out var changeStart, out var removedLength, out var insertedLength)
                    ? EditorTextMarkerOps.ApplyTextChange(markers, changeStart, removedLength, insertedLength, buffer.Length)
                    : [];
            cursorIndex = EditorTextMarkerOps.SnapCaretToBoundary(markers, cursorIndex, buffer.Length);
            if (advanceClientRevision) {
                AdvanceClientBufferRevision();
            }
        }

        private void AdvanceClientBufferRevision() {
            clientBufferRevision = checked(clientBufferRevision + 1);
        }

        private bool HasSameMarkerCatalog(IReadOnlyList<ProjectionMarkerCatalogItem> other) {
            if (markerCatalog.Length != other.Count) {
                return false;
            }

            for (var index = 0; index < markerCatalog.Length; index++) {
                var left = markerCatalog[index];
                var right = other[index];
                if (!string.Equals(left.Key, right.Key, StringComparison.Ordinal)
                    || !string.Equals(left.VariantKey ?? string.Empty, right.VariantKey ?? string.Empty, StringComparison.Ordinal)
                    || !string.Equals(left.DisplayText, right.DisplayText, StringComparison.Ordinal)
                    || !string.Equals(left.Style?.Key, right.Style?.Key, StringComparison.Ordinal)) {
                    return false;
                }
            }

            return true;
        }

        private bool TryMaterializeLeadingMarker(char typedChar) {
            if (!EditorTextMarkerOps.TryFindMarkerAtCursor(markers, cursorIndex, out var marker)
                || ResolveMarkerCatalogItem(marker.Key, marker.VariantKey) is not { } catalogItem
                || catalogItem.DisplayText.Length != 1
                || catalogItem.DisplayText[0] != typedChar) {
                return false;
            }

            ReplaceMarker(marker, catalogItem.DisplayText, cursorIndex + catalogItem.DisplayText.Length);
            OnBufferMutated(preservePopupAssistCompletion: true);
            return true;
        }

        private void RemoveMarker(ClientBufferedTextMarker marker, bool keepCursorAtStart) {
            ReplaceMarker(marker, string.Empty, keepCursorAtStart ? marker.StartIndex : cursorIndex);
            OnBufferMutated(preservePopupAssistCompletion: true);
        }

        private void ReplaceMarker(ClientBufferedTextMarker marker, string replacementText, int caretAfterReplace) {
            var markerStart = Math.Clamp(marker.StartIndex, 0, buffer.Length);
            var markerLength = Math.Clamp(marker.Length, 0, buffer.Length - markerStart);
            buffer.Remove(markerStart, markerLength);
            if (!string.IsNullOrEmpty(replacementText)) {
                buffer.Insert(markerStart, replacementText);
            }

            cursorIndex = Math.Clamp(caretAfterReplace, 0, buffer.Length);
            markers = [.. markers
                .Where(existing => existing.StartIndex != marker.StartIndex
                    || existing.Length != marker.Length
                    || !string.Equals(existing.Key, marker.Key, StringComparison.Ordinal)
                    || !string.Equals(existing.VariantKey ?? string.Empty, marker.VariantKey ?? string.Empty, StringComparison.Ordinal))];
            markers = EditorTextMarkerOps.ApplyTextChange(markers, markerStart, markerLength, replacementText?.Length ?? 0, buffer.Length);
            cursorIndex = EditorTextMarkerOps.SnapCaretToBoundary(markers, cursorIndex, buffer.Length);
        }

        private ProjectionMarkerCatalogItem? ResolveMarkerCatalogItem(string key, string variantKey) {
            var normalizedVariant = variantKey ?? string.Empty;
            return markerCatalog.FirstOrDefault(item =>
                string.Equals(item.Key, key, StringComparison.Ordinal)
                && string.Equals(item.VariantKey ?? string.Empty, normalizedVariant, StringComparison.Ordinal));
        }

        private static bool TryResolveTextChange(
            string previousText,
            string nextText,
            out int changeStart,
            out int removedLength,
            out int insertedLength) {
            changeStart = 0;
            removedLength = 0;
            insertedLength = 0;
            if (string.Equals(previousText, nextText, StringComparison.Ordinal)) {
                return true;
            }

            var previous = previousText ?? string.Empty;
            var next = nextText ?? string.Empty;
            var prefixLength = 0;
            var prefixLimit = Math.Min(previous.Length, next.Length);
            while (prefixLength < prefixLimit && previous[prefixLength] == next[prefixLength]) {
                prefixLength++;
            }

            var suffixLength = 0;
            while (suffixLength < previous.Length - prefixLength
                && suffixLength < next.Length - prefixLength
                && previous[previous.Length - suffixLength - 1] == next[next.Length - suffixLength - 1]) {
                suffixLength++;
            }

            changeStart = prefixLength;
            removedLength = previous.Length - prefixLength - suffixLength;
            insertedLength = next.Length - prefixLength - suffixLength;
            return true;
        }

        private void ResetHistoryNavigation() {
            GetHistory().Index = -1;
        }

        private void ResetCompletion(bool clearPendingRequest = true) {
            completionActive = false;
            completionIndex = -1;
            globalCompletionIndex = 0;
            pinnedCompletionSignature = default;
            requestedPageOffset = pageWindowOffset;
            if (clearPendingRequest) {
                pendingCompletionOpenRequest = CompletionOpenRequest.None;
            }
        }

        private void ResetPagingState() {
            pagingActive = false;
            pagedCandidateCount = 0;
            pageWindowOffset = 0;
            requestedPageOffset = 0;
            globalCompletionIndex = 0;
        }

        private EditorHistory GetHistory() {
            return editorKind == EditorPaneKind.MultiLine ? multiLineHistory : singleLineHistory;
        }
    }
}
