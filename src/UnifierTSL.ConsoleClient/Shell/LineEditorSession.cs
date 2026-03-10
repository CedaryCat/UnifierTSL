using System.Text;
using UnifierTSL.ConsoleClient.Shared.ConsolePrompting;

namespace UnifierTSL.ConsoleClient.Shell
{
    public enum LineEditorInputActionKind
    {
        None,
        Redraw,
        Submit,
        Autocomplete,
        ScrollStatus,
        Cancel,
    }

    public readonly record struct LineEditorInputAction(
        LineEditorInputActionKind Kind,
        string? Payload = null,
        int Delta = 0,
        bool ForceRawSubmit = false)
    {
        public static readonly LineEditorInputAction None = new(LineEditorInputActionKind.None);
        public static readonly LineEditorInputAction Redraw = new(LineEditorInputActionKind.Redraw);
        public static readonly LineEditorInputAction Autocomplete = new(LineEditorInputActionKind.Autocomplete);
        public static readonly LineEditorInputAction Cancel = new(LineEditorInputActionKind.Cancel);
        public static LineEditorInputAction Submit(string line, bool forceRawSubmit = false) => new(LineEditorInputActionKind.Submit, line, 0, forceRawSubmit);
        public static LineEditorInputAction ScrollStatus(int delta) => new(LineEditorInputActionKind.ScrollStatus, null, delta);
    }

    public readonly record struct LineEditorRenderState(
        string Text,
        int CursorIndex,
        string GhostSuffix,
        int CompletionIndex,
        int CompletionCount);

    public sealed class LineEditorSession
    {
        private const int MaxHistoryItems = 200;

        private readonly List<string> history = [];
        private readonly StringBuilder buffer = new();

        private int cursorIndex;
        private int historyIndex = -1;
        private string historyDraft = string.Empty;

        private bool completionActive;
        private List<string> completionCandidates = [];
        private int completionIndex = -1;
        private bool pagingActive;
        private int pagedTotalCandidateCount;
        private int pagedPageSize = 1;
        private int pagedPrefetchThreshold;
        private int pagedActualWindowOffset;
        private int pagedRequestedWindowOffset;
        private int pagedGlobalCompletionIndex;

        private string staticGhostText = string.Empty;
        private bool enableCtrlEnterBypassGhostFallback = true;
        private Func<string, IReadOnlyList<string>> suggestionProvider = _ => Array.Empty<string>();

        public void SetSuggestionProvider(Func<string, IReadOnlyList<string>> provider) {
            suggestionProvider = provider ?? (_ => Array.Empty<string>());
        }

        public void BeginNewLine(string? ghostText, bool ctrlEnterBypassGhostFallback = true) {
            buffer.Clear();
            cursorIndex = 0;
            staticGhostText = ghostText ?? string.Empty;
            enableCtrlEnterBypassGhostFallback = ctrlEnterBypassGhostFallback;
            ResetPagingState();
            ResetCompletion();
            ResetHistoryNavigation();
        }

        public void SyncPagedCompletionWindow(ConsoleSuggestionPageState pagingState) {
            ArgumentNullException.ThrowIfNull(pagingState);

            if (!pagingState.Enabled) {
                ClearPagedCompletionWindow();
                return;
            }

            pagingActive = true;
            pagedTotalCandidateCount = Math.Max(0, pagingState.TotalCandidateCount);
            pagedPageSize = Math.Max(1, pagingState.PageSize);
            pagedPrefetchThreshold = Math.Clamp(pagingState.PrefetchThreshold, 0, pagedPageSize - 1);
            int maxOffset = ResolveMaxPagedWindowOffset(pagedTotalCandidateCount, pagedPageSize);
            pagedActualWindowOffset = Math.Clamp(pagingState.WindowOffset, 0, maxOffset);
            pagedRequestedWindowOffset = pagedActualWindowOffset;

            string currentText = buffer.ToString();
            completionCandidates = ResolveSuggestions(currentText);

            if (pagingState.SelectedWindowIndex <= 0 || pagedTotalCandidateCount == 0) {
                completionActive = false;
                completionIndex = -1;
                pagedGlobalCompletionIndex = 0;
                return;
            }

            completionActive = true;
            pagedGlobalCompletionIndex = Math.Clamp(
                pagedActualWindowOffset + pagingState.SelectedWindowIndex,
                1,
                pagedTotalCandidateCount);
            SyncLocalCompletionIndex();
        }

        public LineEditorRenderState GetRenderState() {
            string text = buffer.ToString();
            int completionSelectionCount = completionActive ? GetCompletionSelectionCount() : 0;
            return new LineEditorRenderState(
                Text: text,
                CursorIndex: cursorIndex,
                GhostSuffix: BuildGhostSuffix(text),
                CompletionIndex: completionSelectionCount > 0 ? GetSelectedCompletionOrdinal() : 0,
                CompletionCount: completionSelectionCount);
        }

        public ConsoleInputState BuildInputState(ConsoleInputPurpose purpose) {
            string text = buffer.ToString();
            int completionSelectionCount = completionActive ? GetCompletionSelectionCount() : 0;
            int completionSelectionIndex = completionSelectionCount > 0 ? GetSelectedCompletionOrdinal() : 0;
            return new ConsoleInputState {
                Purpose = purpose,
                InputText = text,
                CursorIndex = cursorIndex,
                CompletionIndex = completionSelectionIndex,
                CompletionCount = completionSelectionCount,
                CandidateWindowOffset = pagingActive && completionSelectionIndex > 0
                    ? pagedRequestedWindowOffset
                    : 0,
            };
        }

        public LineEditorInputAction ApplyKey(ConsoleKeyInfo key) {
            if ((key.Modifiers & ConsoleModifiers.Control) != 0) {
                return HandleControlKey(key.Key);
            }

            return key.Key switch {
                ConsoleKey.Enter => SubmitCurrentLine(),
                ConsoleKey.Backspace => HandleBackspace(),
                ConsoleKey.Delete => HandleDelete(),
                ConsoleKey.LeftArrow => MoveCursor(-1),
                ConsoleKey.RightArrow => HandleRightArrow(),
                ConsoleKey.Home => MoveCursorTo(0),
                ConsoleKey.End => MoveCursorTo(buffer.Length),
                ConsoleKey.UpArrow => completionActive ? CycleCompletion(reverse: true) : HandleHistoryUp(),
                ConsoleKey.DownArrow => completionActive ? CycleCompletion(reverse: false) : HandleHistoryDown(),
                ConsoleKey.Tab => HandleAutocomplete(reverse: (key.Modifiers & ConsoleModifiers.Shift) != 0),
                ConsoleKey.Escape => CancelCompletion(),
                _ => HandlePrintableKey(key),
            };
        }

        private LineEditorInputAction HandleControlKey(ConsoleKey key) {
            return key switch {
                ConsoleKey.Enter => SubmitCurrentLine(forceRawSubmit: enableCtrlEnterBypassGhostFallback),
                ConsoleKey.A => MoveCursorTo(0),
                ConsoleKey.E => MoveCursorTo(buffer.Length),
                ConsoleKey.U => ClearLine(),
                ConsoleKey.C => LineEditorInputAction.Cancel,
                ConsoleKey.UpArrow => LineEditorInputAction.ScrollStatus(-1),
                ConsoleKey.DownArrow => LineEditorInputAction.ScrollStatus(1),
                _ => LineEditorInputAction.None,
            };
        }

        private LineEditorInputAction ClearLine() {
            if (buffer.Length == 0) {
                return LineEditorInputAction.None;
            }

            buffer.Clear();
            cursorIndex = 0;
            ResetCompletion();
            ResetHistoryNavigation();
            return LineEditorInputAction.Redraw;
        }

        private LineEditorInputAction HandlePrintableKey(ConsoleKeyInfo key) {
            if (char.IsControl(key.KeyChar) || (key.Modifiers & ConsoleModifiers.Alt) != 0) {
                return LineEditorInputAction.None;
            }

            buffer.Insert(cursorIndex, key.KeyChar);
            cursorIndex += 1;
            ResetCompletion();
            ResetHistoryNavigation();
            return LineEditorInputAction.Redraw;
        }

        private LineEditorInputAction HandleBackspace() {
            if (cursorIndex == 0 || buffer.Length == 0) {
                return LineEditorInputAction.None;
            }

            buffer.Remove(cursorIndex - 1, 1);
            cursorIndex -= 1;
            ResetCompletion();
            ResetHistoryNavigation();
            return LineEditorInputAction.Redraw;
        }

        private LineEditorInputAction HandleDelete() {
            if (cursorIndex >= buffer.Length) {
                return LineEditorInputAction.None;
            }

            buffer.Remove(cursorIndex, 1);
            ResetCompletion();
            ResetHistoryNavigation();
            return LineEditorInputAction.Redraw;
        }

        private LineEditorInputAction HandleRightArrow() {
            if (cursorIndex == buffer.Length && TryAcceptCompletionCandidate()) {
                return LineEditorInputAction.Redraw;
            }

            return MoveCursor(1);
        }

        private LineEditorInputAction MoveCursor(int delta) {
            int target = Math.Clamp(cursorIndex + delta, 0, buffer.Length);
            if (target == cursorIndex) {
                return LineEditorInputAction.None;
            }

            cursorIndex = target;
            ResetCompletion();
            return LineEditorInputAction.Redraw;
        }

        private LineEditorInputAction MoveCursorTo(int target) {
            int bounded = Math.Clamp(target, 0, buffer.Length);
            if (bounded == cursorIndex) {
                return LineEditorInputAction.None;
            }

            cursorIndex = bounded;
            ResetCompletion();
            return LineEditorInputAction.Redraw;
        }

        private LineEditorInputAction HandleHistoryUp() {
            if (history.Count == 0) {
                return LineEditorInputAction.None;
            }

            ResetCompletion();

            if (historyIndex == -1) {
                historyDraft = buffer.ToString();
                historyIndex = history.Count - 1;
            }
            else if (historyIndex > 0) {
                historyIndex -= 1;
            }
            else {
                return LineEditorInputAction.None;
            }

            ReplaceText(history[historyIndex]);
            return LineEditorInputAction.Redraw;
        }

        private LineEditorInputAction HandleHistoryDown() {
            if (history.Count == 0 || historyIndex == -1) {
                return LineEditorInputAction.None;
            }

            ResetCompletion();

            if (historyIndex < history.Count - 1) {
                historyIndex += 1;
                ReplaceText(history[historyIndex]);
            }
            else {
                historyIndex = -1;
                ReplaceText(historyDraft);
            }

            return LineEditorInputAction.Redraw;
        }

        private LineEditorInputAction HandleAutocomplete(bool reverse) {
            if (cursorIndex != buffer.Length) {
                return LineEditorInputAction.None;
            }

            if (!EnsureCompletionCandidates()) {
                return LineEditorInputAction.None;
            }

            int completionCount = GetCompletionSelectionCount();
            if (completionCount <= 0) {
                ResetCompletion();
                return LineEditorInputAction.None;
            }

            int currentSelection = GetSelectedCompletionOrdinal();
            int nextSelection = currentSelection <= 0 || currentSelection > completionCount
                ? reverse ? completionCount : 1
                : reverse
                    ? (currentSelection - 2 + completionCount) % completionCount + 1
                    : currentSelection % completionCount + 1;

            SetSelectedCompletionOrdinal(nextSelection);
            return LineEditorInputAction.Autocomplete;
        }

        private LineEditorInputAction CycleCompletion(bool reverse) {
            if (!completionActive) {
                return LineEditorInputAction.None;
            }

            int completionCount = GetCompletionSelectionCount();
            if (completionCount <= 0) {
                ResetCompletion();
                return LineEditorInputAction.None;
            }

            int currentSelection = GetSelectedCompletionOrdinal();
            int nextSelection = currentSelection <= 0 || currentSelection > completionCount
                ? reverse ? completionCount : 1
                : reverse
                    ? (currentSelection - 2 + completionCount) % completionCount + 1
                    : currentSelection % completionCount + 1;

            SetSelectedCompletionOrdinal(nextSelection);
            return LineEditorInputAction.Autocomplete;
        }

        private LineEditorInputAction CancelCompletion() {
            if (!completionActive) {
                return LineEditorInputAction.None;
            }

            ResetCompletion();
            return LineEditorInputAction.Redraw;
        }

        private bool EnsureCompletionCandidates() {
            string currentText = buffer.ToString();
            List<string> suggestions = ResolveSuggestions(currentText);

            if (suggestions.Count == 0) {
                ResetCompletion();
                return false;
            }

            if (completionActive && suggestions.SequenceEqual(completionCandidates, StringComparer.OrdinalIgnoreCase)) {
                SyncLocalCompletionIndex();
                if ((pagingActive && pagedGlobalCompletionIndex > 0)
                    || completionIndex >= 0 && completionIndex < completionCandidates.Count) {
                    return true;
                }
            }

            completionCandidates = suggestions;
            SyncLocalCompletionIndex();
            return true;
        }

        private bool TryAcceptCompletionCandidate() {
            string currentText = buffer.ToString();
            string? candidate = GetCandidateForDisplay(currentText, includePreviewCandidate: true);
            if (string.IsNullOrEmpty(candidate)) {
                return false;
            }

            if (candidate.Length <= currentText.Length) {
                return false;
            }

            ReplaceText(candidate);
            ResetCompletion();
            ResetHistoryNavigation();
            return true;
        }

        private string BuildGhostSuffix(string currentText) {
            if (cursorIndex != buffer.Length) {
                return string.Empty;
            }

            string? candidate = GetCandidateForDisplay(currentText, includePreviewCandidate: true);
            if (string.IsNullOrEmpty(candidate)) {
                return string.Empty;
            }

            if (candidate.Length <= currentText.Length) {
                return string.Empty;
            }

            if (!candidate.StartsWith(currentText, StringComparison.OrdinalIgnoreCase)) {
                return string.Empty;
            }

            return candidate[currentText.Length..];
        }

        private string? GetCandidateForDisplay(string currentText, bool includePreviewCandidate) {
            if (completionActive && completionIndex >= 0 && completionIndex < completionCandidates.Count) {
                return completionCandidates[completionIndex];
            }

            if (completionActive && pagingActive && pagedGlobalCompletionIndex > 0) {
                return null;
            }

            if (string.IsNullOrEmpty(currentText) && !string.IsNullOrWhiteSpace(staticGhostText)) {
                return staticGhostText;
            }

            if (!includePreviewCandidate) {
                return null;
            }

            List<string> preview = ResolveSuggestions(currentText);
            if (preview.Count == 0) {
                return null;
            }

            return preview[0];
        }

        private List<string> ResolveSuggestions(string currentText) {
            return [.. ConsoleTextSetOps.DistinctPreserveOrder(suggestionProvider(currentText))];
        }

        private void ClearPagedCompletionWindow() {
            if (!pagingActive) {
                return;
            }

            bool hadResolvedSelection = completionActive
                && completionIndex >= 0
                && completionIndex < completionCandidates.Count;
            ResetPagingState();
            if (!hadResolvedSelection) {
                completionActive = false;
                completionIndex = -1;
            }
        }

        private int GetCompletionSelectionCount() {
            return pagingActive
                ? pagedTotalCandidateCount
                : completionCandidates.Count;
        }

        private int GetSelectedCompletionOrdinal() {
            if (!completionActive) {
                return 0;
            }

            return pagingActive
                ? pagedGlobalCompletionIndex
                : completionIndex >= 0
                    ? completionIndex + 1
                    : 0;
        }

        private void SetSelectedCompletionOrdinal(int selectionOrdinal) {
            int completionCount = GetCompletionSelectionCount();
            if (completionCount <= 0) {
                ResetCompletion();
                return;
            }

            int normalizedSelection = Math.Clamp(selectionOrdinal, 1, completionCount);
            completionActive = true;
            if (pagingActive) {
                pagedGlobalCompletionIndex = normalizedSelection;
                pagedRequestedWindowOffset = ResolvePagedWindowOffset(
                    selectionGlobalIndex: normalizedSelection - 1,
                    requestedOffset: pagedRequestedWindowOffset,
                    totalCandidateCount: pagedTotalCandidateCount,
                    pageSize: pagedPageSize,
                    prefetchThreshold: pagedPrefetchThreshold);
            }

            completionIndex = pagingActive
                ? ResolvePagedLocalCompletionIndex(normalizedSelection)
                : normalizedSelection - 1;
        }

        private void SyncLocalCompletionIndex() {
            if (!completionActive) {
                completionIndex = -1;
                return;
            }

            if (pagingActive) {
                completionIndex = ResolvePagedLocalCompletionIndex(pagedGlobalCompletionIndex);
                return;
            }

            if (completionIndex >= 0 && completionIndex < completionCandidates.Count) {
                return;
            }

            completionIndex = -1;
        }

        private int ResolvePagedLocalCompletionIndex(int selectionOrdinal) {
            if (!pagingActive || selectionOrdinal <= 0) {
                return -1;
            }

            int localIndex = selectionOrdinal - pagedActualWindowOffset - 1;
            return localIndex >= 0 && localIndex < completionCandidates.Count
                ? localIndex
                : -1;
        }

        private static int ResolveMaxPagedWindowOffset(int totalCandidateCount, int pageSize) {
            return Math.Max(0, totalCandidateCount - Math.Max(1, pageSize));
        }

        private static int ResolvePagedWindowOffset(
            int selectionGlobalIndex,
            int requestedOffset,
            int totalCandidateCount,
            int pageSize,
            int prefetchThreshold) {
            if (selectionGlobalIndex < 0 || totalCandidateCount <= 0) {
                return 0;
            }

            int boundedPageSize = Math.Max(1, pageSize);
            int threshold = Math.Clamp(prefetchThreshold, 0, boundedPageSize - 1);
            int maxOffset = ResolveMaxPagedWindowOffset(totalCandidateCount, boundedPageSize);
            int boundedRequestedOffset = Math.Clamp(requestedOffset, 0, maxOffset);
            int boundedSelection = Math.Clamp(selectionGlobalIndex, 0, totalCandidateCount - 1);
            int local = boundedSelection - boundedRequestedOffset;
            if (local <= threshold && boundedRequestedOffset > 0) {
                return Math.Max(0, boundedSelection - threshold);
            }

            int forwardThreshold = boundedPageSize - threshold - 1;
            if (local >= forwardThreshold && boundedRequestedOffset < maxOffset) {
                return Math.Min(maxOffset, boundedSelection - forwardThreshold);
            }

            return boundedRequestedOffset;
        }

        private LineEditorInputAction SubmitCurrentLine(bool forceRawSubmit = false) {
            string line = buffer.ToString();
            if (!string.IsNullOrWhiteSpace(line)) {
                if (history.Count == 0 || !string.Equals(history[^1], line, StringComparison.Ordinal)) {
                    history.Add(line);
                    if (history.Count > MaxHistoryItems) {
                        history.RemoveAt(0);
                    }
                }
            }

            buffer.Clear();
            cursorIndex = 0;
            ResetCompletion();
            ResetHistoryNavigation();
            return LineEditorInputAction.Submit(line, forceRawSubmit);
        }

        private void ReplaceText(string text) {
            buffer.Clear();
            buffer.Append(text);
            cursorIndex = buffer.Length;
        }

        private void ResetHistoryNavigation() {
            historyIndex = -1;
        }

        private void ResetCompletion() {
            completionActive = false;
            completionCandidates = [];
            completionIndex = -1;
            pagedGlobalCompletionIndex = 0;
            pagedRequestedWindowOffset = pagedActualWindowOffset;
        }

        private void ResetPagingState() {
            pagingActive = false;
            pagedTotalCandidateCount = 0;
            pagedPageSize = 1;
            pagedPrefetchThreshold = 0;
            pagedActualWindowOffset = 0;
            pagedRequestedWindowOffset = 0;
            pagedGlobalCompletionIndex = 0;
        }
    }
}
