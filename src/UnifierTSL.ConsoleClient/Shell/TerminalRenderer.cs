using UnifierTSL.ConsoleClient.Shared.ConsolePrompting;

namespace UnifierTSL.ConsoleClient.Shell
{
    public sealed class TerminalRenderer
    {
        private bool footerDrawn;
        private int footerTopRow = -1;
        private int footerTotalRows;
        private int inputRow = -1;
        private int footerCursorRow = -1;

        public bool FooterDrawn => footerDrawn;
        // Exposed so ConsoleShell can align its output anchor when reservation caused implicit scroll.
        public int FooterTopRow => footerTopRow;

        public void Reset() {
            footerDrawn = false;
            footerTopRow = -1;
            footerTotalRows = 0;
            inputRow = -1;
            footerCursorRow = -1;
        }

        public void Draw(
            ConsoleRenderSnapshot render,
            LineEditorRenderState inputState,
            IReadOnlyList<string> statusLines,
            bool showInputRow,
            bool showInputCaret,
            bool supportsVirtualTerminal,
            int? anchorTopRow = null,
            ConsolePromptTheme? statusHeaderThemeOverride = null) {
            ReconcileFooterTopRowFromCursor();
            SetCursorVisibleSafe(false);

            int writableWidth = GetWritableWidth();
            int desiredRows = statusLines.Count + (showInputRow ? 1 : 0);

            if (anchorTopRow is int requestedTopRow) {
                int normalizedTop = ClampRow(requestedTopRow);
                if (footerDrawn && footerTopRow >= 0 && footerTopRow != normalizedTop) {
                    ClearFooterArea();
                }

                footerTopRow = normalizedTop;
            }

            int previousFooterTotalRows = footerDrawn ? footerTotalRows : 0;
            if (!footerDrawn || footerTopRow < 0) {
                if (anchorTopRow is null) {
                    footerTopRow = ClampRow(Console.CursorTop);
                }

                SetCursorPositionSafe(0, footerTopRow);
                ReserveFooterRows(desiredRows);
            }
            else if (desiredRows > footerTotalRows) {
                ExtendFooterRows(desiredRows);
            }

            ConsolePromptTheme theme = render.Payload.Theme ?? ConsolePromptTheme.Default;
            ConsolePromptTheme statusHeaderTheme = statusHeaderThemeOverride ?? theme;
            SetCursorPositionSafe(0, footerTopRow);
            for (int index = 0; index < statusLines.Count; index++) {
                WriteStatusLine(
                    statusLines[index],
                    index == 0,
                    writableWidth,
                    supportsVirtualTerminal,
                    index == 0 ? statusHeaderTheme : theme);
                if (index < statusLines.Count - 1) {
                    SetCursorPositionSafe(0, footerTopRow + index + 1);
                }
            }

            for (int index = desiredRows; index < previousFooterTotalRows; index++) {
                ClearLineAt(footerTopRow + index);
            }

            if (showInputRow) {
                inputRow = footerTopRow + statusLines.Count;
                SetCursorPositionSafe(0, inputRow);
                int cursorColumn = WriteInputLine(render, inputState, writableWidth, supportsVirtualTerminal, theme);
                SetCursorPositionSafe(cursorColumn, inputRow);
                SetCursorVisibleSafe(showInputCaret);
                footerCursorRow = GetCursorTopOrFallback(inputRow);
            }
            else {
                inputRow = -1;
                int cursorRow = ClampRow(footerTopRow + Math.Max(0, statusLines.Count - 1));
                SetCursorPositionSafe(0, cursorRow);
                SetCursorVisibleSafe(false);
                footerCursorRow = GetCursorTopOrFallback(cursorRow);
            }

            footerTotalRows = desiredRows;
            footerDrawn = true;
        }

        public void ClearFooterArea() {
            ReconcileFooterTopRowFromCursor();

            if (!footerDrawn || footerTopRow < 0 || footerTotalRows <= 0) {
                return;
            }

            int bufferHeight = GetBufferHeight();
            for (int index = 0; index < footerTotalRows; index++) {
                int row = footerTopRow + index;
                if (row < 0 || row >= bufferHeight) {
                    continue;
                }

                ClearLineAt(row);
            }

            SetCursorPositionSafe(0, footerTopRow);
            footerDrawn = false;
            inputRow = -1;
            footerCursorRow = -1;
        }

        private void ReserveFooterRows(int rows) {
            int boundedRows = Math.Max(1, rows);
            for (int index = 0; index < boundedRows - 1; index++) {
                Console.WriteLine();
            }

            footerTopRow = ClampRow(GetCursorTopOrFallback(footerTopRow) - (boundedRows - 1));
        }

        private void ExtendFooterRows(int rows) {
            int boundedRows = Math.Max(1, rows);
            int extraRows = boundedRows - footerTotalRows;
            if (extraRows <= 0) {
                return;
            }

            int bottomRow = ClampRow(footerTopRow + Math.Max(0, footerTotalRows - 1));
            SetCursorPositionSafe(0, bottomRow);
            for (int index = 0; index < extraRows; index++) {
                Console.WriteLine();
            }

            footerTopRow = ClampRow(GetCursorTopOrFallback(bottomRow) - (boundedRows - 1));
        }

        private static void ClearLineAt(int row) {
            int writableWidth = GetWritableWidth();
            SetCursorPositionSafe(0, row);
            Console.Write(new string(' ', writableWidth));
            SetCursorPositionSafe(0, row);
        }

        private static void WriteStatusLine(
            string line,
            bool isHeader,
            int writableWidth,
            bool supportsVirtualTerminal,
            ConsolePromptTheme theme) {
            string source = line ?? string.Empty;
            bool useVividStatusBar = isHeader && supportsVirtualTerminal && theme.UseVividStatusBar;
            string visible = AnsiSanitizer.StripAnsi(source);
            bool passThroughAnsi = supportsVirtualTerminal
                && (!isHeader || useVividStatusBar)
                && AnsiSanitizer.ContainsEscape(source);
            if (passThroughAnsi) {
                if (TerminalCellWidth.Measure(visible) > writableWidth) {
                    source = visible;
                    visible = source;
                    passThroughAnsi = false;
                }
            }

            string fitted;
            int fittedWidth;
            if (passThroughAnsi) {
                fitted = source;
                fittedWidth = TerminalCellWidth.Measure(visible);
            }
            else {
                fitted = PadRightByCellWidth(FitText(visible, writableWidth), writableWidth);
                fittedWidth = TerminalCellWidth.Measure(fitted);
            }
            int tailPadding = Math.Max(0, writableWidth - fittedWidth);

            Console.Write('\r');
            if (supportsVirtualTerminal) {
                string statusDetailsBase = $"{AnsiColorCodec.Escape}{AnsiColorCodec.GetForegroundCode(theme.StatusDetailForeground)}m";
                if (isHeader) {
                    ConsoleColor foreground = useVividStatusBar ? theme.VividStatusBarForeground : theme.StatusBarForeground;
                    ConsoleColor background = useVividStatusBar ? theme.VividStatusBarBackground : theme.StatusBarBackground;
                    string statusBarBase = AnsiColorCodec.GetSgr(foreground, background);
                    Console.Write(statusBarBase);
                    if (passThroughAnsi) {
                        string recolored = source.Replace(AnsiColorCodec.Reset, statusBarBase, StringComparison.Ordinal);
                        Console.Write(recolored);
                        if (tailPadding > 0) {
                            Console.Write(statusBarBase);
                            Console.Write(new string(' ', tailPadding));
                        }
                    }
                    else {
                        Console.Write(fitted);
                    }
                    Console.Write(AnsiColorCodec.Reset);
                }
                else if (passThroughAnsi) {
                    string tinted = fitted.Replace(AnsiColorCodec.Reset, statusDetailsBase, StringComparison.Ordinal);
                    Console.Write(statusDetailsBase);
                    Console.Write(tinted);
                    if (tailPadding > 0) {
                        Console.Write(statusDetailsBase);
                        Console.Write(new string(' ', tailPadding));
                    }
                    Console.Write(AnsiColorCodec.Reset);
                }
                else {
                    Console.Write(statusDetailsBase);
                    Console.Write(fitted);
                    Console.Write(AnsiColorCodec.Reset);
                }
            }
            else {
                if (isHeader) {
                    WriteWithConsoleColors(fitted, theme.StatusBarForeground, theme.StatusBarBackground);
                }
                else {
                    Console.Write(fitted);
                }
            }
            Console.Write('\r');
        }

        private static int WriteInputLine(
            ConsoleRenderSnapshot render,
            LineEditorRenderState inputState,
            int writableWidth,
            bool supportsVirtualTerminal,
            ConsolePromptTheme theme) {
            string prompt = string.IsNullOrEmpty(render.Payload.Prompt) ? "> " : render.Payload.Prompt;
            string safePrompt = AnsiSanitizer.SanitizeEscapes(prompt);

            int promptWidth = TerminalCellWidth.Measure(safePrompt);
            int inputWidth = Math.Max(1, writableWidth - promptWidth);
            VisibleInputViewport viewport = BuildVisibleInput(inputState, inputWidth);
            int occupiedWidth = promptWidth
                + TerminalCellWidth.Measure(viewport.Typed)
                + TerminalCellWidth.Measure(viewport.Ghost);

            if (supportsVirtualTerminal) {
                Console.Write('\r');
                WriteForegroundText(safePrompt, theme.PromptForeground, bold: true);
                WriteTypedViewport(render, inputState, viewport, theme);
                WriteForegroundText(viewport.Ghost, theme.GhostForeground);
            }
            else {
                Console.Write('\r');
                Console.Write(safePrompt);
                Console.Write(viewport.Typed);
                Console.Write(viewport.Ghost);
            }

            int baseCompletionCount = inputState.CompletionCount;
            int displayCompletionIndex = inputState.CompletionIndex;
            int displayCompletionCount = inputState.CompletionCount;
            if (render.Paging.Enabled && baseCompletionCount > 0) {
                displayCompletionCount = render.Paging.TotalCandidateCount;
                if (displayCompletionIndex > 0) {
                    displayCompletionIndex = render.Paging.WindowOffset + displayCompletionIndex;
                }
            }

            if (baseCompletionCount > 0) {
                string badge = $" [{displayCompletionIndex}/{displayCompletionCount}]";
                int badgeWidth = TerminalCellWidth.Measure(badge);
                if (occupiedWidth + badgeWidth <= writableWidth) {
                    if (supportsVirtualTerminal) {
                        WriteForegroundText(badge, theme.SuggestionBadgeForeground);
                    }
                    else {
                        Console.Write(badge);
                    }

                    occupiedWidth += badgeWidth;
                }
            }

            if (occupiedWidth < writableWidth) {
                Console.Write(new string(' ', writableWidth - occupiedWidth));
            }

            return Math.Clamp(promptWidth + viewport.CursorOffset, 0, writableWidth);
        }

        private static void WriteTypedViewport(
            ConsoleRenderSnapshot render,
            LineEditorRenderState inputState,
            VisibleInputViewport viewport,
            ConsolePromptTheme theme) {
            if (string.IsNullOrEmpty(viewport.Typed)) {
                return;
            }

            if (render.Payload.Purpose != ConsoleInputPurpose.CommandLine ||
                !ConsoleCommandInputClassifier.TryFindCommandTokenSpan(
                    inputState.Text,
                    render.Payload.CommandPrefixes,
                    out ConsoleCommandTokenSpan commandSpan)) {
                WriteForegroundText(viewport.Typed, theme.InputForeground);
                return;
            }

            int visibleStart = viewport.TypedSourceStartIndex;
            int visibleEnd = visibleStart + viewport.Typed.Length;
            int highlightStart = Math.Max(visibleStart, commandSpan.StartIndex);
            int highlightEnd = Math.Min(visibleEnd, commandSpan.EndIndex);

            if (highlightStart >= highlightEnd) {
                WriteForegroundText(viewport.Typed, theme.InputForeground);
                return;
            }

            int prefixLength = highlightStart - visibleStart;
            int highlightLength = highlightEnd - highlightStart;
            if (prefixLength > 0) {
                WriteForegroundText(viewport.Typed[..prefixLength], theme.InputForeground);
            }

            WriteForegroundText(
                viewport.Typed.Substring(prefixLength, highlightLength),
                theme.CommandForeground);

            int suffixStart = prefixLength + highlightLength;
            if (suffixStart < viewport.Typed.Length) {
                WriteForegroundText(viewport.Typed[suffixStart..], theme.InputForeground);
            }
        }

        private static void WriteForegroundText(string text, ConsoleColor color, bool bold = false) {
            if (string.IsNullOrEmpty(text)) {
                return;
            }

            Console.Write(AnsiColorCodec.Escape);
            Console.Write(AnsiColorCodec.GetForegroundCode(color));
            if (bold) {
                Console.Write(";1");
            }

            Console.Write('m');
            Console.Write(text);
            Console.Write(AnsiColorCodec.Reset);
        }

        private static void WriteWithConsoleColors(string text, ConsoleColor foreground, ConsoleColor background) {
            ConsoleColor previousForeground = Console.ForegroundColor;
            ConsoleColor previousBackground = Console.BackgroundColor;
            try {
                Console.ForegroundColor = foreground;
                Console.BackgroundColor = background;
                Console.Write(text);
            }
            finally {
                Console.ForegroundColor = previousForeground;
                Console.BackgroundColor = previousBackground;
            }
        }

        internal static int GetWritableWidth() {
            try {
                return Math.Max(10, Console.WindowWidth - 1);
            }
            catch {
                return 119;
            }
        }

        internal static int ClampRow(int row) {
            try {
                return Math.Clamp(row, 0, Math.Max(0, Console.BufferHeight - 1));
            }
            catch {
                return Math.Max(0, row);
            }
        }

        internal static int ClampColumn(int column) {
            try {
                return Math.Clamp(column, 0, Math.Max(0, Console.BufferWidth - 1));
            }
            catch {
                return Math.Max(0, column);
            }
        }

        private static int GetBufferHeight() {
            try {
                return Math.Max(1, Console.BufferHeight);
            }
            catch {
                return 1;
            }
        }

        private static int GetCursorTopOrFallback(int fallback) {
            try {
                return Console.CursorTop;
            }
            catch {
                return fallback;
            }
        }

        private static void SetCursorPositionSafe(int left, int top) {
            try {
                Console.SetCursorPosition(ClampColumn(left), ClampRow(top));
            }
            catch {
            }
        }

        private static void SetCursorVisibleSafe(bool visible) {
            try {
                Console.CursorVisible = visible;
            }
            catch {
            }
        }

        private void ReconcileFooterTopRowFromCursor() {
            if (!footerDrawn || footerTopRow < 0 || footerCursorRow < 0) {
                return;
            }

            int currentCursorTop = GetCursorTopOrFallback(footerCursorRow);
            int implicitShift = currentCursorTop - footerCursorRow;
            if (implicitShift == 0) {
                return;
            }

            footerTopRow += implicitShift;
            if (inputRow >= 0) {
                inputRow += implicitShift;
            }

            footerCursorRow = currentCursorTop;
        }

        private static string FitText(string input, int maxWidth) {
            if (maxWidth <= 0) {
                return string.Empty;
            }

            if (string.IsNullOrEmpty(input)) {
                return string.Empty;
            }

            int inputWidth = TerminalCellWidth.Measure(input);
            if (inputWidth <= maxWidth) {
                return input;
            }

            if (maxWidth <= 3) {
                int visibleLength = TerminalCellWidth.TakeUtf16LengthByColumns(input, 0, maxWidth, out _);
                return visibleLength > 0 ? input[..visibleLength] : string.Empty;
            }

            int contentLength = TerminalCellWidth.TakeUtf16LengthByColumns(input, 0, maxWidth - 3, out _);
            string visible = contentLength > 0 ? input[..contentLength] : string.Empty;
            return visible + "...";
        }

        private static string PadRightByCellWidth(string input, int targetWidth) {
            int width = TerminalCellWidth.Measure(input);
            if (width >= targetWidth) {
                return input;
            }

            return input + new string(' ', targetWidth - width);
        }

        private static VisibleInputViewport BuildVisibleInput(LineEditorRenderState state, int maxWidth) {
            if (maxWidth <= 0) {
                return new VisibleInputViewport(string.Empty, string.Empty, 0, 0);
            }

            string text = state.Text ?? string.Empty;
            bool showGhost = state.CursorIndex == text.Length && !string.IsNullOrEmpty(state.GhostSuffix);
            string composite = showGhost ? text + state.GhostSuffix : text;
            int typedLength = text.Length;
            int cursorIndex = Math.Clamp(state.CursorIndex, 0, text.Length);
            int cursorOffset = TerminalCellWidth.MeasurePrefix(text, cursorIndex);
            int compositeWidth = TerminalCellWidth.Measure(composite);

            if (compositeWidth <= maxWidth) {
                string typed = composite[..Math.Min(typedLength, composite.Length)];
                string ghost = composite.Length > typed.Length ? composite[typed.Length..] : string.Empty;
                return new VisibleInputViewport(typed, ghost, cursorOffset, 0);
            }

            int maxStart = Math.Max(0, compositeWidth - maxWidth);
            int targetStart = Math.Clamp(cursorOffset - maxWidth + 1, 0, maxStart);
            int windowStart = TerminalCellWidth.FindUtf16IndexForColumns(composite, targetStart, out _);
            int windowLength = TerminalCellWidth.TakeUtf16LengthByColumns(composite, windowStart, maxWidth, out _);

            string visible = windowLength > 0
                ? composite.Substring(windowStart, windowLength)
                : string.Empty;
            char[] chars = visible.ToCharArray();

            if (chars.Length > 0) {
                if (windowStart > 0) {
                    chars[0] = '.';
                }

                if (windowStart + windowLength < composite.Length) {
                    chars[^1] = '.';
                }
            }

            visible = new(chars);
            int typedVisibleCount = Math.Clamp(Math.Min(windowStart + windowLength, typedLength) - windowStart, 0, visible.Length);
            string typedPart = typedVisibleCount > 0 ? visible[..typedVisibleCount] : string.Empty;
            string ghostPart = typedVisibleCount < visible.Length ? visible[typedVisibleCount..] : string.Empty;
            int cursorVisibleLength = Math.Clamp(cursorIndex - windowStart, 0, visible.Length);
            int cursorVisibleOffset = TerminalCellWidth.Measure(visible.AsSpan(0, cursorVisibleLength));

            return new VisibleInputViewport(
                typedPart,
                ghostPart,
                cursorVisibleOffset,
                Math.Min(windowStart, typedLength));
        }

        private readonly record struct VisibleInputViewport(
            string Typed,
            string Ghost,
            int CursorOffset,
            int TypedSourceStartIndex);
    }
}
