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

        public void Reset()
        {
            footerDrawn = false;
            footerTopRow = -1;
            footerTotalRows = 0;
            inputRow = -1;
            footerCursorRow = -1;
        }

        public void Draw(
            ReadLineRenderSnapshot render,
            LineEditorRenderState inputState,
            IReadOnlyList<string> statusLines,
            bool showInputRow,
            bool supportsVirtualTerminal,
            int? anchorTopRow = null)
        {
            ReconcileFooterTopRowFromCursor();

            int writableWidth = GetWritableWidth();
            int desiredRows = statusLines.Count + (showInputRow ? 1 : 0);

            if (anchorTopRow is int requestedTopRow) {
                int normalizedTop = ClampRow(requestedTopRow);
                if (footerDrawn && footerTopRow >= 0 && footerTopRow != normalizedTop) {
                    ClearFooterArea();
                }

                footerTopRow = normalizedTop;
            }

            if (!footerDrawn || footerTopRow < 0) {
                if (anchorTopRow is null) {
                    footerTopRow = ClampRow(Console.CursorTop);
                }

                SetCursorPositionSafe(0, footerTopRow);
                ReserveFooterRows(desiredRows);
            }
            else {
                int redrawTop = footerTopRow;
                ClearFooterArea();
                SetCursorPositionSafe(0, redrawTop);
                footerTopRow = redrawTop;
                if (desiredRows != footerTotalRows) {
                    ReserveFooterRows(desiredRows);
                }
            }

            SetCursorPositionSafe(0, footerTopRow);
            for (int index = 0; index < statusLines.Count; index++) {
                WriteStatusLine(
                    statusLines[index],
                    index == 0,
                    writableWidth,
                    supportsVirtualTerminal,
                    render.Payload.AllowAnsiStatusEscapes);
                if (index < statusLines.Count - 1) {
                    SetCursorPositionSafe(0, footerTopRow + index + 1);
                }
            }

            if (showInputRow) {
                inputRow = footerTopRow + statusLines.Count;
                SetCursorPositionSafe(0, inputRow);
                int cursorColumn = WriteInputLine(render, inputState, writableWidth, supportsVirtualTerminal);
                SetCursorPositionSafe(cursorColumn, inputRow);
                Console.CursorVisible = true;
                footerCursorRow = GetCursorTopOrFallback(inputRow);
            }
            else {
                inputRow = -1;
                int cursorRow = ClampRow(footerTopRow + Math.Max(0, statusLines.Count - 1));
                SetCursorPositionSafe(0, cursorRow);
                Console.CursorVisible = false;
                footerCursorRow = GetCursorTopOrFallback(cursorRow);
            }

            footerTotalRows = desiredRows;
            footerDrawn = true;
        }

        public void ClearFooterArea()
        {
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

        private void ReserveFooterRows(int rows)
        {
            int boundedRows = Math.Max(1, rows);
            for (int index = 0; index < boundedRows - 1; index++) {
                Console.WriteLine();
            }

            footerTopRow = ClampRow(GetCursorTopOrFallback(footerTopRow) - (boundedRows - 1));
        }

        private static void ClearLineAt(int row)
        {
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
            bool allowAnsiStatusEscapes)
        {
            string source = line ?? string.Empty;
            bool passThroughAnsi = allowAnsiStatusEscapes
                && supportsVirtualTerminal
                && AnsiSanitizer.ContainsEscape(source);

            string fitted;
            if (passThroughAnsi) {
                fitted = source;
            }
            else {
                string normalized = allowAnsiStatusEscapes
                    ? AnsiSanitizer.StripAnsi(source)
                    : AnsiSanitizer.SanitizeEscapes(source);
                fitted = FitText(normalized, writableWidth).PadRight(writableWidth);
            }

            Console.Write('\r');
            if (passThroughAnsi) {
                Console.Write(new string(' ', writableWidth));
                Console.Write('\r');
            }
            if (supportsVirtualTerminal) {
                if (isHeader) {
                    Console.Write("\u001b[30;106m");
                    Console.Write(fitted);
                    Console.Write(AnsiColorCodec.Reset);
                }
                else if (passThroughAnsi) {
                    string tinted = fitted.Replace(AnsiColorCodec.Reset, "\u001b[90m", StringComparison.Ordinal);
                    Console.Write("\u001b[90m");
                    Console.Write(tinted);
                    Console.Write(AnsiColorCodec.Reset);
                }
                else {
                    Console.Write("\u001b[90m");
                    Console.Write(fitted);
                    Console.Write(AnsiColorCodec.Reset);
                }
            }
            else {
                Console.Write(fitted);
            }
            Console.Write('\r');
        }

        private static int WriteInputLine(ReadLineRenderSnapshot render, LineEditorRenderState inputState, int writableWidth, bool supportsVirtualTerminal)
        {
            string prompt = string.IsNullOrEmpty(render.Payload.Prompt) ? "> " : render.Payload.Prompt;
            string safePrompt = AnsiSanitizer.SanitizeEscapes(prompt);

            int promptWidth = TerminalCellWidth.Measure(safePrompt);
            int inputWidth = Math.Max(1, writableWidth - promptWidth);
            VisibleInputViewport viewport = BuildVisibleInput(inputState, inputWidth);

            Console.Write('\r');
            Console.Write(new string(' ', writableWidth));
            Console.Write('\r');

            if (supportsVirtualTerminal) {
                Console.Write("\u001b[92;1m");
                Console.Write(safePrompt);
                Console.Write(AnsiColorCodec.Reset);

                if (viewport.Typed.Length > 0) {
                    Console.Write("\u001b[97m");
                    Console.Write(viewport.Typed);
                    Console.Write(AnsiColorCodec.Reset);
                }

                if (viewport.Ghost.Length > 0) {
                    Console.Write("\u001b[90m");
                    Console.Write(viewport.Ghost);
                    Console.Write(AnsiColorCodec.Reset);
                }
            }
            else {
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
                int contentWidth = promptWidth + TerminalCellWidth.Measure(viewport.Typed) + TerminalCellWidth.Measure(viewport.Ghost);
                if (contentWidth + badge.Length <= writableWidth) {
                    if (supportsVirtualTerminal) {
                        Console.Write("\u001b[90m");
                        Console.Write(badge);
                        Console.Write(AnsiColorCodec.Reset);
                    }
                    else {
                        Console.Write(badge);
                    }
                }
            }

            return Math.Clamp(promptWidth + viewport.CursorOffset, 0, writableWidth);
        }

        private static int GetWritableWidth()
        {
            try {
                return Math.Max(10, Console.WindowWidth - 1);
            }
            catch {
                return 119;
            }
        }

        private static int ClampRow(int row)
        {
            try {
                return Math.Clamp(row, 0, Math.Max(0, Console.BufferHeight - 1));
            }
            catch {
                return Math.Max(0, row);
            }
        }

        private static int ClampColumn(int column)
        {
            try {
                return Math.Clamp(column, 0, Math.Max(0, Console.BufferWidth - 1));
            }
            catch {
                return Math.Max(0, column);
            }
        }

        private static int GetBufferHeight()
        {
            try {
                return Math.Max(1, Console.BufferHeight);
            }
            catch {
                return 1;
            }
        }

        private static int GetCursorTopOrFallback(int fallback)
        {
            try {
                return Console.CursorTop;
            }
            catch {
                return fallback;
            }
        }

        private static void SetCursorPositionSafe(int left, int top)
        {
            try {
                Console.SetCursorPosition(ClampColumn(left), ClampRow(top));
            }
            catch {
            }
        }

        private void ReconcileFooterTopRowFromCursor()
        {
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

        private static string FitText(string input, int maxWidth)
        {
            if (maxWidth <= 0) {
                return string.Empty;
            }

            if (string.IsNullOrEmpty(input)) {
                return string.Empty;
            }

            if (input.Length <= maxWidth) {
                return input;
            }

            if (maxWidth <= 3) {
                return input[..maxWidth];
            }

            return input[..(maxWidth - 3)] + "...";
        }

        private static VisibleInputViewport BuildVisibleInput(LineEditorRenderState state, int maxWidth)
        {
            if (maxWidth <= 0) {
                return new VisibleInputViewport(string.Empty, string.Empty, 0);
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
                return new VisibleInputViewport(typed, ghost, cursorOffset);
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

            return new VisibleInputViewport(typedPart, ghostPart, cursorVisibleOffset);
        }

        private readonly record struct VisibleInputViewport(string Typed, string Ghost, int CursorOffset);
    }
}
