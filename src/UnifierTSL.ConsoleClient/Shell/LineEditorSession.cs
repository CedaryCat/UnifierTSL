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

        private string staticGhostText = string.Empty;
        private bool enableCtrlEnterBypassGhostFallback = true;
        private Func<string, IReadOnlyList<string>> suggestionProvider = _ => Array.Empty<string>();

        public void SetSuggestionProvider(Func<string, IReadOnlyList<string>> provider)
        {
            suggestionProvider = provider ?? (_ => Array.Empty<string>());
        }

        public void BeginNewLine(string? ghostText, bool ctrlEnterBypassGhostFallback = true)
        {
            buffer.Clear();
            cursorIndex = 0;
            staticGhostText = ghostText ?? string.Empty;
            enableCtrlEnterBypassGhostFallback = ctrlEnterBypassGhostFallback;
            ResetCompletion();
            ResetHistoryNavigation();
        }

        public void SyncPagedCompletionWindow(int selectedWindowIndex)
        {
            if (selectedWindowIndex <= 0) {
                ResetCompletion();
                return;
            }

            string currentText = buffer.ToString();
            List<string> suggestions = ResolveSuggestions(currentText);
            if (suggestions.Count == 0) {
                ResetCompletion();
                return;
            }

            completionCandidates = suggestions;
            completionIndex = Math.Clamp(selectedWindowIndex - 1, 0, completionCandidates.Count - 1);
            completionActive = true;
        }

        public LineEditorRenderState GetRenderState()
        {
            string text = buffer.ToString();
            return new LineEditorRenderState(
                Text: text,
                CursorIndex: cursorIndex,
                GhostSuffix: BuildGhostSuffix(text),
                CompletionIndex: completionIndex >= 0 ? completionIndex + 1 : 0,
                CompletionCount: completionCandidates.Count);
        }

        public LineEditorInputAction ApplyKey(ConsoleKeyInfo key)
        {
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

        private LineEditorInputAction HandleControlKey(ConsoleKey key)
        {
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

        private LineEditorInputAction ClearLine()
        {
            if (buffer.Length == 0) {
                return LineEditorInputAction.None;
            }

            buffer.Clear();
            cursorIndex = 0;
            ResetCompletion();
            ResetHistoryNavigation();
            return LineEditorInputAction.Redraw;
        }

        private LineEditorInputAction HandlePrintableKey(ConsoleKeyInfo key)
        {
            if (char.IsControl(key.KeyChar) || (key.Modifiers & ConsoleModifiers.Alt) != 0) {
                return LineEditorInputAction.None;
            }

            buffer.Insert(cursorIndex, key.KeyChar);
            cursorIndex += 1;
            ResetCompletion();
            ResetHistoryNavigation();
            return LineEditorInputAction.Redraw;
        }

        private LineEditorInputAction HandleBackspace()
        {
            if (cursorIndex == 0 || buffer.Length == 0) {
                return LineEditorInputAction.None;
            }

            buffer.Remove(cursorIndex - 1, 1);
            cursorIndex -= 1;
            ResetCompletion();
            ResetHistoryNavigation();
            return LineEditorInputAction.Redraw;
        }

        private LineEditorInputAction HandleDelete()
        {
            if (cursorIndex >= buffer.Length) {
                return LineEditorInputAction.None;
            }

            buffer.Remove(cursorIndex, 1);
            ResetCompletion();
            ResetHistoryNavigation();
            return LineEditorInputAction.Redraw;
        }

        private LineEditorInputAction HandleRightArrow()
        {
            if (cursorIndex == buffer.Length && TryAcceptCompletionCandidate()) {
                return LineEditorInputAction.Redraw;
            }

            return MoveCursor(1);
        }

        private LineEditorInputAction MoveCursor(int delta)
        {
            int target = Math.Clamp(cursorIndex + delta, 0, buffer.Length);
            if (target == cursorIndex) {
                return LineEditorInputAction.None;
            }

            cursorIndex = target;
            ResetCompletion();
            return LineEditorInputAction.Redraw;
        }

        private LineEditorInputAction MoveCursorTo(int target)
        {
            int bounded = Math.Clamp(target, 0, buffer.Length);
            if (bounded == cursorIndex) {
                return LineEditorInputAction.None;
            }

            cursorIndex = bounded;
            ResetCompletion();
            return LineEditorInputAction.Redraw;
        }

        private LineEditorInputAction HandleHistoryUp()
        {
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

        private LineEditorInputAction HandleHistoryDown()
        {
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

        private LineEditorInputAction HandleAutocomplete(bool reverse)
        {
            if (cursorIndex != buffer.Length) {
                return LineEditorInputAction.None;
            }

            if (!EnsureCompletionCandidates()) {
                return LineEditorInputAction.None;
            }

            if (completionIndex < 0 || completionIndex >= completionCandidates.Count) {
                completionIndex = reverse ? completionCandidates.Count - 1 : 0;
            }
            else {
                completionIndex = reverse
                    ? (completionIndex - 1 + completionCandidates.Count) % completionCandidates.Count
                    : (completionIndex + 1) % completionCandidates.Count;
            }

            completionActive = true;
            return LineEditorInputAction.Autocomplete;
        }

        private LineEditorInputAction CycleCompletion(bool reverse)
        {
            if (!completionActive || completionCandidates.Count == 0) {
                ResetCompletion();
                return LineEditorInputAction.None;
            }

            completionIndex = reverse
                ? (completionIndex - 1 + completionCandidates.Count) % completionCandidates.Count
                : (completionIndex + 1) % completionCandidates.Count;

            return LineEditorInputAction.Autocomplete;
        }

        private LineEditorInputAction CancelCompletion()
        {
            if (!completionActive) {
                return LineEditorInputAction.None;
            }

            ResetCompletion();
            return LineEditorInputAction.Redraw;
        }

        private bool EnsureCompletionCandidates()
        {
            string currentText = buffer.ToString();
            List<string> suggestions = ResolveSuggestions(currentText);

            if (suggestions.Count == 0) {
                ResetCompletion();
                return false;
            }

            if (completionActive && suggestions.SequenceEqual(completionCandidates, StringComparer.OrdinalIgnoreCase)) {
                if (completionIndex >= 0 && completionIndex < completionCandidates.Count) {
                    return true;
                }
            }

            completionCandidates = suggestions;
            completionIndex = -1;
            completionActive = true;
            return true;
        }

        private bool TryAcceptCompletionCandidate()
        {
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

        private string BuildGhostSuffix(string currentText)
        {
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

        private string? GetCandidateForDisplay(string currentText, bool includePreviewCandidate)
        {
            if (string.IsNullOrEmpty(currentText) && !string.IsNullOrWhiteSpace(staticGhostText)) {
                return staticGhostText;
            }

            if (completionActive && completionIndex >= 0 && completionIndex < completionCandidates.Count) {
                return completionCandidates[completionIndex];
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

        private List<string> ResolveSuggestions(string currentText)
        {
            return [.. ConsoleTextSetOps.DistinctPreserveOrder(suggestionProvider(currentText))];
        }

        private LineEditorInputAction SubmitCurrentLine(bool forceRawSubmit = false)
        {
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

        private void ReplaceText(string text)
        {
            buffer.Clear();
            buffer.Append(text);
            cursorIndex = buffer.Length;
        }

        private void ResetHistoryNavigation()
        {
            historyIndex = -1;
        }

        private void ResetCompletion()
        {
            completionActive = false;
            completionCandidates = [];
            completionIndex = -1;
        }
    }
}
