using UnifierTSL.ConsoleClient.Shared.ConsolePrompting;

namespace UnifierTSL.ConsoleClient.Shell
{
    public sealed class TerminalRenderer
    {
        private bool footerDrawn;
        private int footerTopRow = -1;
        private FooterSnapshot? footerSnapshot;
        private bool rebaseAfterReservationDrop;
        private const int FooterUnsafeClearMarginRows = 2;

        public bool FooterDrawn => footerDrawn;
        // Exposed so ConsoleShell can align its output anchor when footer reservation or reflow moved it.
        public int FooterTopRow => footerTopRow;

        public void Reset() {
            footerDrawn = false;
            footerTopRow = -1;
            footerSnapshot = null;
            rebaseAfterReservationDrop = false;
        }

        public bool RequiresViewportRefresh() {
            if (!footerDrawn || footerSnapshot is null) {
                return false;
            }

            return footerSnapshot.DrawnWritableWidth != GetWritableWidth()
                || footerSnapshot.DrawnWrapWidth != GetWrapWidth();
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
            FooterLayout previousLayout = ResolveCurrentFooterLayout();
            FooterSnapshot? previousSnapshot = footerSnapshot;
            SetCursorVisibleSafe(false);

            int writableWidth = GetWritableWidth();
            int wrapWidth = GetWrapWidth();
            int desiredRows = statusLines.Count + (showInputRow ? 1 : 0);
            int? normalizedAnchor = anchorTopRow is int requestedTopRow
                ? ClampRow(requestedTopRow)
                : null;

            bool hasPreviousLayout = previousLayout.IsValid && previousSnapshot is not null;
            bool anchorChanged = normalizedAnchor is int requestedAnchor
                && hasPreviousLayout
                && previousLayout.TopRow != requestedAnchor;
            bool logicalRowCountChanged = hasPreviousLayout
                && previousSnapshot!.LineCellWidths.Length != desiredRows;
            bool viewportWidthChanged = hasPreviousLayout
                && (previousSnapshot!.DrawnWritableWidth != writableWidth
                    || previousSnapshot.DrawnWrapWidth != wrapWidth);

            // A footer reservation is only trustworthy while it still describes the same logical
            // slice of the buffer. If the requested anchor, logical row count, or viewport width
            // changed, clearing/updating in place can target rows that now belong to scrollback or
            // freshly written log lines after terminal reflow.
            if (hasPreviousLayout && (anchorChanged || logicalRowCountChanged || viewportWidthChanged)) {
                string resetReason = string.Join(
                    ",",
                    new[] {
                        anchorChanged ? "anchor" : null,
                        logicalRowCountChanged ? "rows" : null,
                        viewportWidthChanged ? "viewport" : null
                    }.Where(static item => item is not null));
#if UNIFIER_TERMINAL_DEBUG_TRACE
                TerminalDebugTrace.WriteThrottled(
                    "FooterDraw.Reset",
                    $"reason={resetReason} prevTop={previousLayout.TopRow} prevPhysicalRows={previousLayout.PhysicalRowCount} footerTop={footerTopRow} desiredRows={desiredRows} prevLogicalRows={previousSnapshot!.LineCellWidths.Length} anchorReq={(normalizedAnchor is int anchor ? anchor : -1)} cursorTop={GetCursorTopOrFallback(-1)} widths={previousSnapshot.DrawnWritableWidth}/{previousSnapshot.DrawnWrapWidth}->{writableWidth}/{wrapWidth}",
                    minIntervalMs: viewportWidthChanged ? 80 : 160);
#endif
                TraceFooterSlice(
                    "FooterReset.Before",
                    $"reason={resetReason} prevTop={previousLayout.TopRow} prevRows={previousLayout.PhysicalRowCount} desiredRows={desiredRows}",
                    previousLayout.TopRow,
                    previousLayout.PhysicalRowCount);
                if (ShouldAvoidDestructiveClear(previousLayout)) {
                    DropFooterReservationState();
                }
                else {
                    ClearFooterArea(previousLayout, supportsVirtualTerminal);
                }
                TraceFooterSlice(
                    "FooterReset.After",
                    $"reason={resetReason} postTop={footerTopRow} postDrawn={footerDrawn}",
                    previousLayout.TopRow,
                    Math.Max(previousLayout.PhysicalRowCount, desiredRows));
                hasPreviousLayout = false;
                previousSnapshot = null;
            }

            if (!hasPreviousLayout || footerTopRow < 0) {
                int currentCursorTop = ClampRow(GetCursorTopOrFallback(0));
                if (rebaseAfterReservationDrop) {
                    int fallbackAnchorLineIndex = showInputRow
                        ? statusLines.Count
                        : Math.Max(0, statusLines.Count - 1);
                    footerTopRow = normalizedAnchor ?? ClampRow(currentCursorTop - fallbackAnchorLineIndex);
                }
                else {
                    footerTopRow = normalizedAnchor ?? currentCursorTop;
                }
                SetCursorPositionSafe(0, footerTopRow);
                if (rebaseAfterReservationDrop) {
                    rebaseAfterReservationDrop = false;
                }
                else {
                    ReserveFooterRows(desiredRows);
                }
            }
            else {
                footerTopRow = previousLayout.TopRow;
            }

            int[] lineCellWidths = new int[desiredRows];
            int anchorLineIndex = 0;
            int anchorColumn = 0;
            ConsolePromptTheme theme = render.Payload.Theme ?? ConsolePromptTheme.Default;
            ConsolePromptTheme statusHeaderTheme = statusHeaderThemeOverride ?? theme;

            SetCursorPositionSafe(0, footerTopRow);
            for (int index = 0; index < statusLines.Count; index++) {
                int previousLineWidth = previousSnapshot is not null
                    && index < previousSnapshot.LineCellWidths.Length
                    ? Math.Max(0, previousSnapshot.LineCellWidths[index])
                    : 0;
                bool hasFollowingRow = index < statusLines.Count - 1 || showInputRow;
                lineCellWidths[index] = WriteStatusLine(
                    statusLines[index],
                    index == 0,
                    writableWidth,
                    supportsVirtualTerminal,
                    index == 0 ? statusHeaderTheme : theme,
                    previousLineWidth,
                    deferResetToNextRow: index == 0 && hasFollowingRow);
                if (index < statusLines.Count - 1) {
                    SetCursorPositionSafe(0, footerTopRow + index + 1);
                }
            }

            if (showInputRow) {
                int inputRow = footerTopRow + statusLines.Count;
                SetCursorPositionSafe(0, inputRow);
                InputLineRenderResult inputRender = WriteInputLine(
                    render,
                    inputState,
                    writableWidth,
                    supportsVirtualTerminal,
                    theme);
                lineCellWidths[statusLines.Count] = inputRender.RenderedCellWidth;
                SetCursorPositionSafe(inputRender.CursorColumn, inputRow);
                SetCursorVisibleSafe(showInputCaret);
                anchorLineIndex = statusLines.Count;
                anchorColumn = inputRender.CursorColumn;
            }
            else {
                int cursorRow = ClampRow(footerTopRow + Math.Max(0, statusLines.Count - 1));
                SetCursorPositionSafe(0, cursorRow);
                SetCursorVisibleSafe(false);
                anchorLineIndex = Math.Max(0, statusLines.Count - 1);
                anchorColumn = 0;
            }

            footerSnapshot = new FooterSnapshot(
                lineCellWidths,
                anchorLineIndex,
                anchorColumn,
                writableWidth,
                wrapWidth);
            footerDrawn = true;

            if (normalizedAnchor is int finalRequestedAnchor && footerTopRow != finalRequestedAnchor) {
#if UNIFIER_TERMINAL_DEBUG_TRACE
                TerminalDebugTrace.WriteThrottled(
                    "FooterDraw.AnchorDrift",
                    $"requested={finalRequestedAnchor} actual={footerTopRow} desiredRows={desiredRows} anchorLine={anchorLineIndex} anchorColumn={anchorColumn} cursorTop={GetCursorTopOrFallback(-1)} widths={writableWidth}/{wrapWidth}",
                    minIntervalMs: 80);
#endif
            }
        }

        public void ClearFooterArea(bool supportsVirtualTerminal = false) {
            FooterLayout layout = ResolveCurrentFooterLayout();
            if (!layout.IsValid) {
                return;
            }

            if (ShouldAvoidDestructiveClear(layout)) {
                DropFooterReservationState();
                return;
            }

            ClearFooterArea(layout, supportsVirtualTerminal);
        }

        private void ClearFooterArea(FooterLayout layout, bool supportsVirtualTerminal) {
            int spillRowsAbove = EstimateSpillRowsAbove(layout);
            int clearTopRow = Math.Max(0, layout.TopRow - spillRowsAbove);
            int clearRowCount = Math.Max(0, layout.PhysicalRowCount + spillRowsAbove);
            TraceFooterSlice(
                "FooterClear.Before",
                $"top={layout.TopRow} rows={layout.PhysicalRowCount} clearTop={clearTopRow} clearRows={clearRowCount} spillAbove={spillRowsAbove} supportsVt={supportsVirtualTerminal}",
                clearTopRow,
                clearRowCount);
            int bufferHeight = GetBufferHeight();
            for (int index = 0; index < clearRowCount; index++) {
                int row = clearTopRow + index;
                if (row < 0 || row >= bufferHeight) {
                    continue;
                }

                ClearLineAt(row, supportsVirtualTerminal);
            }

            SetCursorPositionSafe(0, layout.TopRow);
            footerDrawn = false;
            footerTopRow = -1;
            footerSnapshot = null;
            rebaseAfterReservationDrop = false;
            TraceFooterSlice(
                "FooterClear.After",
                $"top={layout.TopRow} rows={layout.PhysicalRowCount} clearTop={clearTopRow} clearRows={clearRowCount} spillAbove={spillRowsAbove}",
                clearTopRow,
                clearRowCount);
        }

        private int EstimateSpillRowsAbove(FooterLayout layout) {
            if (footerSnapshot is null || footerSnapshot.LineCellWidths.Length == 0 || !layout.IsValid) {
                return 0;
            }

            int wrapWidth = GetWrapWidth();
            int boundedAnchorLineIndex = Math.Clamp(
                footerSnapshot.AnchorLineIndex,
                0,
                footerSnapshot.LineCellWidths.Length - 1);

            int wrappedRowsBeforeAnchor = 0;
            for (int index = 0; index < boundedAnchorLineIndex; index++) {
                wrappedRowsBeforeAnchor += GetWrappedRowCount(footerSnapshot.LineCellWidths[index], wrapWidth);
            }

            wrappedRowsBeforeAnchor += GetWrappedRowOffset(footerSnapshot.AnchorColumn, wrapWidth);
            int logicalRowsBeforeAnchor = boundedAnchorLineIndex;
            int spillRows = Math.Max(0, wrappedRowsBeforeAnchor - logicalRowsBeforeAnchor);

            // Guardrail: avoid over-clearing if width probes become inconsistent mid-resize.
            return Math.Min(spillRows, footerSnapshot.LineCellWidths.Length);
        }

        private bool ShouldAvoidDestructiveClear(FooterLayout layout) {
            if (footerSnapshot is null) {
                return false;
            }

            int currentWritableWidth = GetWritableWidth();
            int currentWrapWidth = GetWrapWidth();
            bool viewportChanged = footerSnapshot.DrawnWritableWidth != currentWritableWidth
                || footerSnapshot.DrawnWrapWidth != currentWrapWidth;

            int currentTop = ClampRow(GetCursorTopOrFallback(layout.TopRow));
            int currentBottom = currentTop + FooterUnsafeClearMarginRows;
            int currentTopMargin = currentTop - FooterUnsafeClearMarginRows;
            int layoutBottom = layout.TopRow + Math.Max(0, layout.PhysicalRowCount - 1);
            bool layoutNearCursor = layoutBottom >= currentTopMargin && layout.TopRow <= currentBottom;
            if (layoutNearCursor) {
                return false;
            }

            // If the cached footer is no longer near the live cursor, the terminal probably already
            // reflowed or scrolled underneath us. Forgetting the reservation is preferable to
            // blindly blanking the old rows, because a stale clear can erase unrelated history.
#if UNIFIER_TERMINAL_DEBUG_TRACE
            TerminalDebugTrace.WriteThrottled(
                "FooterClear.Degraded",
                $"viewportChanged={viewportChanged} layoutTop={layout.TopRow} layoutBottom={layoutBottom} cursorTop={currentTop} width={footerSnapshot.DrawnWritableWidth}/{footerSnapshot.DrawnWrapWidth}->{currentWritableWidth}/{currentWrapWidth}",
                minIntervalMs: viewportChanged ? 80 : 160);
#endif
            return true;
        }

        private void DropFooterReservationState() {
            int cursorTop = ClampRow(GetCursorTopOrFallback(0));
            TraceFooterSlice(
                "FooterDrop.Before",
                $"cursorTop={cursorTop} drawn={footerDrawn} top={footerTopRow}",
                cursorTop,
                6);
            SetCursorPositionSafe(0, cursorTop);
            footerDrawn = false;
            footerTopRow = -1;
            footerSnapshot = null;
            rebaseAfterReservationDrop = true;
            TraceFooterSlice(
                "FooterDrop.After",
                $"cursorTop={cursorTop} drawn={footerDrawn} top={footerTopRow}",
                cursorTop,
                6);
        }

        private static void TraceFooterSlice(string category, string headline, int topRow, int rowCount) {
#if UNIFIER_TERMINAL_DEBUG_TRACE
            int dumpTop = Math.Max(0, topRow - 2);
            int dumpRows = Math.Max(6, rowCount + 4);
            TerminalDebugTrace.DumpConsoleSlice(category, headline, dumpTop, dumpRows, minIntervalMs: 0);
#endif
        }

        private void ReserveFooterRows(int rows) {
            int boundedRows = Math.Max(1, rows);
            for (int index = 0; index < boundedRows - 1; index++) {
                Console.WriteLine();
            }

            footerTopRow = ClampRow(GetCursorTopOrFallback(footerTopRow) - (boundedRows - 1));
        }

        private static void ClearLineAt(int row, bool supportsVirtualTerminal) {
            SetCursorPositionSafe(0, row);
            if (supportsVirtualTerminal) {
                Console.Write(AnsiColorCodec.Reset);
                Console.Write("\u001b[2K\r");
                return;
            }

            int writableWidth = GetWritableWidth();
            Console.Write(new string(' ', writableWidth));
            SetCursorPositionSafe(0, row);
        }

        private static int WriteStatusLine(
            string line,
            bool isHeader,
            int writableWidth,
            bool supportsVirtualTerminal,
            ConsolePromptTheme theme,
            int previousRenderedWidth,
            bool deferResetToNextRow = false) {
            string source = line ?? string.Empty;
            bool useVividStatusBar = isHeader && supportsVirtualTerminal && theme.UseVividStatusBar;
            string visible = AnsiSanitizer.StripAnsi(source);
            int visibleWidth = TerminalCellWidth.Measure(visible);
            bool passThroughAnsi = supportsVirtualTerminal
                && (!isHeader || useVividStatusBar)
                && AnsiSanitizer.ContainsEscape(source);
            if (passThroughAnsi) {
                // Reserve one safety column to avoid accidental wrap when glyph-width differs by terminal.
                int cautiousLimit = Math.Max(1, writableWidth - 1);
                if (visibleWidth > cautiousLimit) {
                    source = visible;
                    visible = source;
                    visibleWidth = TerminalCellWidth.Measure(visible);
                    passThroughAnsi = false;
                }
            }

            int targetFitWidth = isHeader
                ? Math.Max(1, writableWidth - 1)
                : writableWidth;
            string fittedCore = FitText(visible, targetFitWidth);
            int fittedCoreWidth = TerminalCellWidth.Measure(fittedCore);
            string fitted;
            int renderedContentWidth;
            if (passThroughAnsi) {
                fitted = source;
                renderedContentWidth = Math.Min(writableWidth, visibleWidth);
            }
            else {
                fitted = supportsVirtualTerminal
                    ? fittedCore
                    : PadRightByCellWidth(fittedCore, writableWidth);
                renderedContentWidth = Math.Min(writableWidth, fittedCoreWidth);
            }

            if (supportsVirtualTerminal) {
                Console.Write('\r');
                Console.Write(AnsiColorCodec.Reset);
                string statusDetailsBase = $"{AnsiColorCodec.Escape}{AnsiColorCodec.GetForegroundCode(theme.StatusDetailForeground)}m";
                if (isHeader) {
                    ConsoleColor foreground = useVividStatusBar ? theme.VividStatusBarForeground : theme.StatusBarForeground;
                    ConsoleColor background = useVividStatusBar ? theme.VividStatusBarBackground : theme.StatusBarBackground;
                    string statusBarBase = AnsiColorCodec.GetSgr(foreground, background);
                    Console.Write(statusBarBase);
                    if (passThroughAnsi) {
                        string recolored = source.Replace(AnsiColorCodec.Reset, statusBarBase, StringComparison.Ordinal);
                        Console.Write(recolored);
                    }
                    else {
                        Console.Write(fitted);
                    }

                    // Fill the remainder of the row with the status bar background without explicit padding writes.
                    Console.Write("\u001b[K");
                    if (!deferResetToNextRow) {
                        Console.Write(AnsiColorCodec.Reset);
                    }
                }
                else if (passThroughAnsi) {
                    string tinted = fitted.Replace(AnsiColorCodec.Reset, statusDetailsBase, StringComparison.Ordinal);
                    Console.Write(statusDetailsBase);
                    Console.Write(tinted);

                    int previousBounded = Math.Min(writableWidth, Math.Max(0, previousRenderedWidth));
                    int tailPadding = Math.Max(0, previousBounded - renderedContentWidth);
                    if (tailPadding > 0) {
                        Console.Write(statusDetailsBase);
                        Console.Write(new string(' ', tailPadding));
                    }

                    Console.Write(AnsiColorCodec.Reset);
                }
                else {
                    Console.Write(statusDetailsBase);
                    Console.Write(fitted);

                    int previousBounded = Math.Min(writableWidth, Math.Max(0, previousRenderedWidth));
                    int tailPadding = Math.Max(0, previousBounded - renderedContentWidth);
                    if (tailPadding > 0) {
                        Console.Write(statusDetailsBase);
                        Console.Write(new string(' ', tailPadding));
                    }

                    Console.Write(AnsiColorCodec.Reset);
                }
            }
            else {
                Console.Write('\r');
                if (isHeader) {
                    WriteWithConsoleColors(fitted, theme.StatusBarForeground, theme.StatusBarBackground);
                }
                else {
                    Console.Write(fitted);
                }
            }
            Console.Write('\r');
            return renderedContentWidth;
        }

        private static InputLineRenderResult WriteInputLine(
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
                Console.Write(AnsiColorCodec.Reset);
                Console.Write("\u001b[2K\r");
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

            if (!supportsVirtualTerminal && occupiedWidth < writableWidth) {
                Console.Write(new string(' ', writableWidth - occupiedWidth));
                occupiedWidth = writableWidth;
            }

            return new InputLineRenderResult(
                Math.Clamp(promptWidth + viewport.CursorOffset, 0, writableWidth),
                occupiedWidth);
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

        internal static int GetWrapWidth() {
            try {
                return Math.Max(1, Console.BufferWidth);
            }
            catch {
                try {
                    return Math.Max(1, Console.WindowWidth);
                }
                catch {
                    return GetWritableWidth() + 1;
                }
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

        private FooterLayout ResolveCurrentFooterLayout() {
            if (!footerDrawn || footerTopRow < 0 || footerSnapshot is null || footerSnapshot.LineCellWidths.Length == 0) {
                return default;
            }

            int currentWritableWidth = GetWritableWidth();
            int wrapWidth = GetWrapWidth();
            int boundedAnchorLineIndex = Math.Clamp(
                footerSnapshot.AnchorLineIndex,
                0,
                footerSnapshot.LineCellWidths.Length - 1);
            // Footer rows are explicitly positioned one row at a time via SetCursorPosition.
            // During inverse layout we still treat each logical footer line as exactly one physical
            // row. The renderer owns explicit row-to-row positioning; if we "replayed" terminal
            // wrapping here after a width shrink, we would double-count reflow, push TopRow too far
            // upward, and let clear passes eat pre-footer history.
            int rowsBeforeAnchor = boundedAnchorLineIndex;

            int fallbackCursorTop = ClampRow(footerTopRow + rowsBeforeAnchor);
            int currentCursorTop = GetCursorTopOrFallback(fallbackCursorTop);
            int topRow = currentCursorTop - rowsBeforeAnchor;
            int physicalRowCount = footerSnapshot.LineCellWidths.Length;

            int previousTopRow = footerTopRow;
            footerTopRow = ClampRow(topRow);
            bool viewportChanged = footerSnapshot.DrawnWritableWidth != currentWritableWidth
                || footerSnapshot.DrawnWrapWidth != wrapWidth;
            if (viewportChanged || previousTopRow != footerTopRow) {
#if UNIFIER_TERMINAL_DEBUG_TRACE
                TerminalDebugTrace.WriteThrottled(
                    "FooterLayout.Resolve",
                    $"prevTop={previousTopRow} resolvedTop={footerTopRow} rowsBefore={rowsBeforeAnchor} cursorTop={currentCursorTop} fallbackCursorTop={fallbackCursorTop} physicalRows={physicalRowCount} anchorLine={boundedAnchorLineIndex}/{footerSnapshot.LineCellWidths.Length - 1} anchorColumn={footerSnapshot.AnchorColumn} widths={footerSnapshot.DrawnWritableWidth}/{footerSnapshot.DrawnWrapWidth}->{currentWritableWidth}/{wrapWidth} mode=logical-fixed-row",
                    minIntervalMs: viewportChanged ? 80 : 160);
#endif
            }

            return new FooterLayout(footerTopRow, physicalRowCount);
        }

        private static int GetWrappedRowCount(int lineCellWidth, int wrapWidth) {
            int boundedWrapWidth = Math.Max(1, wrapWidth);
            int boundedLineWidth = Math.Max(0, lineCellWidth);
            if (boundedLineWidth == 0) {
                return 1;
            }

            return 1 + ((boundedLineWidth - 1) / boundedWrapWidth);
        }

        private static int GetWrappedRowOffset(int column, int wrapWidth) {
            int boundedWrapWidth = Math.Max(1, wrapWidth);
            int boundedColumn = Math.Max(0, column);
            return boundedColumn / boundedWrapWidth;
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

        private sealed record FooterSnapshot(
            int[] LineCellWidths,
            int AnchorLineIndex,
            int AnchorColumn,
            int DrawnWritableWidth,
            int DrawnWrapWidth);

        private readonly record struct FooterLayout(int TopRow, int PhysicalRowCount)
        {
            public bool IsValid => PhysicalRowCount > 0;
        }

        private readonly record struct InputLineRenderResult(int CursorColumn, int RenderedCellWidth);

        private readonly record struct VisibleInputViewport(
            string Typed,
            string Ghost,
            int CursorOffset,
            int TypedSourceStartIndex);
    }
}
