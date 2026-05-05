using System.Text;
using UnifierTSL.Contracts.Display;

namespace UnifierTSL.Terminal.Shell
{
    public sealed class TerminalRenderer
    {
        private readonly ITerminalDevice terminalDevice;
        private bool footerDrawn;
        private int footerTopRow = -1;
        private FooterSnapshot? footerSnapshot;
        private const int FooterClearRows = 2;
        private const char PopupBorderHorizontal = '─';
        private const char PopupBorderVertical = '│';
        private const char PopupBorderTopLeft = '┌';
        private const char PopupBorderTopRight = '┐';
        private const char PopupBorderBottomLeft = '└';
        private const char PopupBorderBottomRight = '┘';
        private const char PopupBorderTopTee = '┬';
        private const char PopupBorderBottomTee = '┴';
        private const char PopupBorderRightTee = '├';
        private const char PopupBorderLeftTee = '┤';

        public TerminalRenderer()
            : this(new SystemConsoleTerminalDevice()) {
        }

        internal TerminalRenderer(ITerminalDevice terminalDevice) {
            this.terminalDevice = terminalDevice;
        }

        public bool FooterDrawn => footerDrawn;
        // Exposed so ConsoleShell can align its output anchor when footer reservation or reflow moved it.
        public int FooterTopRow => footerTopRow;

        public void Reset() {
            footerDrawn = false;
            footerTopRow = -1;
            footerSnapshot = null;
        }

        public bool RequiresViewportRefresh() {
            if (!footerDrawn || footerSnapshot is null) {
                return false;
            }

            TerminalViewport viewport = terminalDevice.Viewport;
            return footerSnapshot.DrawnWritableWidth != viewport.WritableWidth
                || footerSnapshot.DrawnWrapWidth != viewport.WrapWidth;
        }

        internal void Draw(
            TerminalRenderScene scene,
            bool showInputRow,
            bool showInputCaret,
            bool supportsVirtualTerminal,
            int? anchorTopRow = null) {
            FooterLayout previousLayout = ResolveCurrentFooterLayout();
            FooterSnapshot? previousSnapshot = footerSnapshot;
            SetCursorVisibleSafe(false);

            TerminalViewport viewport = terminalDevice.Viewport;
            int writableWidth = viewport.WritableWidth;
            int wrapWidth = viewport.WrapWidth;
            InputBlockLayout? inputLayout = showInputRow
                ? BuildInputLayout(
                    scene.Input,
                    writableWidth,
                    supportsVirtualTerminal)
                : null;
            PanePopupMaterial? popupAssistMaterial = inputLayout is not null
                ? scene.PopupAssist
                : null;
            FooterRowSnapshot[] rows = BuildFooterRows(
                scene.StatusLines,
                inputLayout,
                scene.InlineAssistLines,
                writableWidth,
                supportsVirtualTerminal,
                popupAssistMaterial?.Height ?? 0);
            int desiredRows = rows.Length;
            int? normalizedAnchor = anchorTopRow is int requestedTopRow
                ? viewport.ClampRow(requestedTopRow)
                : null;

            bool hasPreviousLayout = previousLayout.IsValid && previousSnapshot is not null;
            bool anchorChanged = normalizedAnchor is int requestedAnchor
                && hasPreviousLayout
                && previousLayout.TopRow != requestedAnchor;
            bool logicalRowCountChanged = hasPreviousLayout
                && previousSnapshot!.RowCount != desiredRows;
            bool rowCountIncreased = logicalRowCountChanged
                && desiredRows > previousSnapshot!.RowCount;
            bool viewportWidthChanged = hasPreviousLayout
                && (previousSnapshot!.DrawnWritableWidth != writableWidth
                    || previousSnapshot.DrawnWrapWidth != wrapWidth);
            int desiredAnchorLineIndex = showInputRow
                ? scene.StatusLines.Count + (inputLayout?.CursorRowIndex ?? 0)
                : Math.Max(0, scene.StatusLines.Count - 1);
            int? resetTopRow = null;

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
                    $"reason={resetReason} prevTop={previousLayout.TopRow} prevPhysicalRows={previousLayout.PhysicalRowCount} footerTop={footerTopRow} desiredRows={desiredRows} prevLogicalRows={previousSnapshot!.RowCount} anchorReq={(normalizedAnchor is int anchor ? anchor : -1)} cursorTop={GetCursorTopSafe(-1)} widths={previousSnapshot.DrawnWritableWidth}/{previousSnapshot.DrawnWrapWidth}->{writableWidth}/{wrapWidth}",
                    minIntervalMs: viewportWidthChanged ? 80 : 160);
#endif
                TraceFooterSlice(
                    "FooterReset.Before",
                    $"reason={resetReason} prevTop={previousLayout.TopRow} prevRows={previousLayout.PhysicalRowCount} desiredRows={desiredRows}",
                    previousLayout.TopRow,
                    previousLayout.PhysicalRowCount);
                if (rowCountIncreased && !anchorChanged && !viewportWidthChanged) {
                    resetTopRow = previousLayout.TopRow;
                    DropFooterReservationState();
                }
                else if (ShouldAvoidDestructiveClear(previousLayout)) {
                    DropFooterReservationState();
                    int currentCursorTop = viewport.ClampRow(GetCursorTopSafe(0));
                    resetTopRow = viewport.ClampRow(currentCursorTop - desiredAnchorLineIndex);
                }
                else {
                    ClearFooterArea(previousLayout, supportsVirtualTerminal);
                    resetTopRow = viewport.ClampRow(GetCursorTopSafe(0));
                }
                resetTopRow = normalizedAnchor ?? resetTopRow;
                TraceFooterSlice(
                    "FooterReset.After",
                    $"reason={resetReason} postTop={footerTopRow} postDrawn={footerDrawn}",
                    previousLayout.TopRow,
                    Math.Max(previousLayout.PhysicalRowCount, desiredRows));
                hasPreviousLayout = false;
                previousSnapshot = null;
            }

            if (!hasPreviousLayout || footerTopRow < 0) {
                int currentCursorTop = viewport.ClampRow(GetCursorTopSafe(0));
                footerTopRow = resetTopRow ?? normalizedAnchor ?? currentCursorTop;
                SetCursorPositionSafe(0, footerTopRow);
                ReserveFooterRows(desiredRows);
            }
            else {
                footerTopRow = previousLayout.TopRow;
            }

            PopupOverlaySnapshot? popupSnapshot = popupAssistMaterial is not null && inputLayout is { } popupLayout
                ? BuildPopupOverlaySnapshot(
                    popupAssistMaterial,
                    popupLayout,
                    footerTopRow,
                    scene.StatusLines.Count,
                    rows.Length,
                    writableWidth)
                : null;
            var popupRowsByAbsoluteRow = BuildPopupRowsByAbsoluteRow(popupSnapshot);
            int anchorLineIndex = desiredAnchorLineIndex;
            int anchorColumn = 0;
            for (int index = 0; index < rows.Length; index++) {
                popupRowsByAbsoluteRow.TryGetValue(footerTopRow + index, out PopupOverlayRow[]? overlayRows);
                SetCursorPositionSafe(0, footerTopRow + index);
                WriteComposedFooterRow(
                    BuildComposedFooterRow(
                        rows[index],
                        overlayRows ?? [],
                        scene.StyleDictionary,
                        writableWidth),
                    supportsVirtualTerminal);
            }

            int cursorRow;
            bool cursorVisible;
            if (showInputRow && inputLayout is { } layout) {
                cursorRow = footerTopRow + scene.StatusLines.Count + layout.CursorRowIndex;
                cursorVisible = showInputCaret;
                anchorColumn = layout.CursorColumn;
            }
            else {
                cursorRow = viewport.ClampRow(footerTopRow + Math.Max(0, scene.StatusLines.Count - 1));
                cursorVisible = false;
                anchorColumn = 0;
            }
            SetCursorPositionSafe(anchorColumn, cursorRow);
            SetCursorVisibleSafe(cursorVisible);

            footerSnapshot = new FooterSnapshot(
                [.. Enumerable.Repeat(writableWidth, rows.Length)],
                rows.Length,
                anchorLineIndex,
                anchorColumn,
                writableWidth,
                wrapWidth);
            footerDrawn = true;

            if (normalizedAnchor is int finalRequestedAnchor && footerTopRow != finalRequestedAnchor) {
#if UNIFIER_TERMINAL_DEBUG_TRACE
                TerminalDebugTrace.WriteThrottled(
                    "FooterDraw.AnchorDrift",
                    $"requested={finalRequestedAnchor} actual={footerTopRow} desiredRows={desiredRows} anchorLine={anchorLineIndex} anchorColumn={anchorColumn} cursorTop={GetCursorTopSafe(-1)} widths={writableWidth}/{wrapWidth}",
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
            TerminalViewport viewport = terminalDevice.Viewport;
            for (int index = 0; index < clearRowCount; index++) {
                int row = clearTopRow + index;
                if (row < 0 || row >= viewport.BufferHeight) {
                    continue;
                }

                ClearLineAt(row, viewport.WritableWidth, supportsVirtualTerminal);
            }

            SetCursorPositionSafe(0, layout.TopRow);
            footerDrawn = false;
            footerTopRow = -1;
            footerSnapshot = null;
            TraceFooterSlice(
                "FooterClear.After",
                $"top={layout.TopRow} rows={layout.PhysicalRowCount} clearTop={clearTopRow} clearRows={clearRowCount} spillAbove={spillRowsAbove}",
                clearTopRow,
                clearRowCount);
        }

        private int EstimateSpillRowsAbove(FooterLayout layout) {
            if (footerSnapshot is null || footerSnapshot.RowCount == 0 || !layout.IsValid) {
                return 0;
            }

            int wrapWidth = terminalDevice.Viewport.WrapWidth;
            int boundedAnchorLineIndex = Math.Clamp(
                footerSnapshot.AnchorLineIndex,
                0,
                footerSnapshot.RowCount - 1);

            int wrappedRowsBeforeAnchor = 0;
            for (int index = 0; index < boundedAnchorLineIndex; index++) {
                wrappedRowsBeforeAnchor += GetWrappedRowCount(footerSnapshot.RenderedRowWidths[index], wrapWidth);
            }

            wrappedRowsBeforeAnchor += GetWrappedRowOffset(footerSnapshot.AnchorColumn, wrapWidth);
            int logicalRowsBeforeAnchor = boundedAnchorLineIndex;
            int spillRows = Math.Max(0, wrappedRowsBeforeAnchor - logicalRowsBeforeAnchor);

            // Guardrail: avoid over-clearing if width probes become inconsistent mid-resize.
            return Math.Min(spillRows, footerSnapshot.RowCount);
        }

        private bool ShouldAvoidDestructiveClear(FooterLayout layout) {
            if (footerSnapshot is null) {
                return false;
            }

            TerminalViewport viewport = terminalDevice.Viewport;
            bool viewportChanged = footerSnapshot.DrawnWritableWidth != viewport.WritableWidth
                || footerSnapshot.DrawnWrapWidth != viewport.WrapWidth;

            int currentTop = viewport.ClampRow(GetCursorTopSafe(layout.TopRow));
            int currentBottom = currentTop + FooterClearRows;
            int currentTopMargin = currentTop - FooterClearRows;
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
                $"viewportChanged={viewportChanged} layoutTop={layout.TopRow} layoutBottom={layoutBottom} cursorTop={currentTop} width={footerSnapshot.DrawnWritableWidth}/{footerSnapshot.DrawnWrapWidth}->{viewport.WritableWidth}/{viewport.WrapWidth}",
                minIntervalMs: viewportChanged ? 80 : 160);
#endif
            return true;
        }

        private void DropFooterReservationState() {
            int cursorTop = terminalDevice.Viewport.ClampRow(GetCursorTopSafe(0));
            TraceFooterSlice(
                "FooterDrop.Before",
                $"cursorTop={cursorTop} drawn={footerDrawn} top={footerTopRow}",
                cursorTop,
                6);
            footerDrawn = false;
            footerTopRow = -1;
            footerSnapshot = null;
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
                terminalDevice.WriteLine(string.Empty);
            }

            footerTopRow = terminalDevice.Viewport.ClampRow(GetCursorTopSafe(footerTopRow) - (boundedRows - 1));
        }

        private void ClearLineAt(int row, int writableWidth, bool supportsVirtualTerminal) {
            SetCursorPositionSafe(0, row);
            if (supportsVirtualTerminal) {
                terminalDevice.Write(AnsiColorCodec.Reset);
                terminalDevice.Write("\u001b[2K\r");
                return;
            }

            terminalDevice.Write(new string(' ', writableWidth));
            SetCursorPositionSafe(0, row);
        }

        private FooterRowSnapshot[] BuildFooterRows(
            IReadOnlyList<StatusRenderLine> statusLines,
            InputBlockLayout? inputLayout,
            IReadOnlyList<StatusRenderLine> overlayLines,
            int writableWidth,
            bool supportsVirtualTerminal,
            int popupCanvasRowCount) {
            int inputRowCount = inputLayout?.PhysicalRowCount ?? 0;
            int popupRows = Math.Max(0, popupCanvasRowCount);
            FooterRowSnapshot[] rows = new FooterRowSnapshot[statusLines.Count + inputRowCount + overlayLines.Count + popupRows];
            for (int index = 0; index < statusLines.Count; index++) {
                rows[index] = BuildStatusRowSnapshot(
                    statusLines[index],
                    writableWidth,
                    supportsVirtualTerminal);
            }

            if (inputLayout is not { } layout) {
                for (int index = 0; index < overlayLines.Count; index++) {
                    rows[statusLines.Count + index] = BuildStatusRowSnapshot(
                        overlayLines[index],
                        writableWidth,
                        supportsVirtualTerminal);
                }

                int popupStart = statusLines.Count + overlayLines.Count;
                if (popupRows > 0) {
                    string popupSpacer = new(' ', Math.Max(1, writableWidth));
                    for (int index = 0; index < popupRows; index++) {
                        rows[popupStart + index] = BuildStatusRowSnapshot(
                            StatusRenderLine.FromText(popupSpacer),
                            writableWidth,
                            supportsVirtualTerminal);
                    }
                }

                return rows;
            }

            for (int index = 0; index < layout.Rows.Length; index++) {
                rows[statusLines.Count + index] = BuildInputRowSnapshot(
                    layout.Rows[index]);
            }

            int overlayStart = statusLines.Count + layout.Rows.Length;
            for (int index = 0; index < overlayLines.Count; index++) {
                rows[overlayStart + index] = BuildStatusRowSnapshot(
                    overlayLines[index],
                    writableWidth,
                    supportsVirtualTerminal);
            }

            int popupCanvasStart = overlayStart + overlayLines.Count;
            if (popupRows > 0) {
                string popupSpacer = new(' ', Math.Max(1, writableWidth));
                for (int index = 0; index < popupRows; index++) {
                    rows[popupCanvasStart + index] = BuildStatusRowSnapshot(
                        StatusRenderLine.FromText(popupSpacer),
                        writableWidth,
                        supportsVirtualTerminal);
                }
            }

            return rows;
        }

        private FooterRowSnapshot BuildStatusRowSnapshot(
            StatusRenderLine line,
            int writableWidth,
            bool supportsVirtualTerminal) {
            return line.StyledText is StyledTextLine styledText
                ? BuildStyledStatusRowSnapshot(
                    styledText,
                    writableWidth,
                    supportsVirtualTerminal)
                : BuildPlainStatusRowSnapshot(
                    line.Text,
                    line.LineStyleId,
                    writableWidth,
                    supportsVirtualTerminal);
        }

        private FooterRowSnapshot BuildPlainStatusRowSnapshot(
            string line,
            string lineStyleId,
            int writableWidth,
            bool supportsVirtualTerminal) {
            string source = AnsiSanitizer.SanitizeEscapes(line);
            string visible = source;
            int targetFitWidth = writableWidth;
            string fittedCore = TerminalTextLayoutOps.FitText(visible, targetFitWidth);
            var statusRow = new StatusRenderRow(
                fittedCore,
                [],
                lineStyleId);

            return new FooterRowSnapshot(
                statusRow,
                null);
        }

        private FooterRowSnapshot BuildStyledStatusRowSnapshot(
            StyledTextLine line,
            int writableWidth,
            bool supportsVirtualTerminal) {
            int targetFitWidth = writableWidth;
            StyledTextRun[] fittedRuns = TerminalTextLayoutOps.FitStyledTextRuns(line, targetFitWidth);
            var statusRow = new StatusRenderRow(
                string.Empty,
                fittedRuns,
                line.LineStyleId);

            return new FooterRowSnapshot(
                statusRow,
                null);
        }

        private FooterRowSnapshot BuildInputRowSnapshot(InputRenderRow row) {
            return new FooterRowSnapshot(
                null,
                row);
        }

        private static Dictionary<int, PopupOverlayRow[]> BuildPopupRowsByAbsoluteRow(PopupOverlaySnapshot? popupSnapshot) {
            if (popupSnapshot is null || popupSnapshot.Rows.Length == 0) {
                return [];
            }

            Dictionary<int, List<PopupOverlayRow>> rowsByAbsoluteRow = [];
            foreach (PopupOverlayRow row in popupSnapshot.Rows) {
                if (!rowsByAbsoluteRow.TryGetValue(row.Row, out List<PopupOverlayRow>? rows)) {
                    rows = [];
                    rowsByAbsoluteRow[row.Row] = rows;
                }

                rows.Add(row);
            }

            return rowsByAbsoluteRow.ToDictionary(static entry => entry.Key, static entry => entry.Value.ToArray());
        }

        private FooterCompositeCell[] BuildComposedFooterRow(
            FooterRowSnapshot row,
            IReadOnlyList<PopupOverlayRow> overlayRows,
            StyleDictionary styleDictionary,
            int writableWidth) {
            FooterCompositeCell[] cells = CreateFooterCompositeCells(
                writableWidth,
                row.Status is { } status
                    ? ResolveStatusLineStyle(styleDictionary, status.LineStyleId)
                    : FooterCellStyle.Reset);
            ApplyBaseFooterCells(cells, row, styleDictionary);
            foreach (PopupOverlayRow overlayRow in overlayRows) {
                ApplyPopupOverlayCells(cells, overlayRow, styleDictionary);
            }

            return cells;
        }

        private void WriteComposedFooterRow(FooterCompositeCell[] cells, bool supportsVirtualTerminal) {
            if (supportsVirtualTerminal) {
                terminalDevice.Write(ComposeVirtualFooterCells(cells));
                return;
            }

            WriteConsoleFooterCells(cells);
        }

        private void ApplyBaseFooterCells(
            FooterCompositeCell[] cells,
            FooterRowSnapshot row,
            StyleDictionary styleDictionary) {
            if (row.Status is { } status) {
                ApplyStatusFooterCells(cells, status, styleDictionary);
                return;
            }

            if (row.Input is { } input) {
                ApplyInputFooterCells(cells, input, styleDictionary);
            }
        }

        private void ApplyStatusFooterCells(
            FooterCompositeCell[] cells,
            StatusRenderRow row,
            StyleDictionary styleDictionary) {
            if (row.IsStyled) {
                int column = 0;
                foreach (var run in row.StyledRuns) {
                    if (string.IsNullOrEmpty(run.Text)) {
                        continue;
                    }

                    PutTextIntoFooterCells(
                        cells,
                        column,
                        run.Text,
                        ResolveStatusRunStyle(styleDictionary, row.LineStyleId, run.StyleId));
                    column += TerminalCellWidth.Measure(run.Text);
                }

                return;
            }

            if (!string.IsNullOrEmpty(row.RenderText)) {
                PutTextIntoFooterCells(cells, 0, row.RenderText, ResolveStatusLineStyle(styleDictionary, row.LineStyleId));
            }
        }

        private void ApplyInputFooterCells(
            FooterCompositeCell[] cells,
            InputRenderRow row,
            StyleDictionary styleDictionary) {
            int column = 0;
            if (!string.IsNullOrEmpty(row.PrefixText)) {
                if (row.PrefixStyleSlots.Length == 0) {
                    PutTextIntoFooterCells(
                        cells,
                        0,
                        row.PrefixText,
                        FooterCellStyle.Reset);
                    column += TerminalCellWidth.Measure(row.PrefixText);
                }
                else {
                    foreach (PopupStyledRun run in BuildPopupStyledRuns(row.PrefixText, row.PrefixStyleSlots, new ushort[row.PrefixText.Length])) {
                        if (string.IsNullOrEmpty(run.Text)) {
                            continue;
                        }

                        var style = ResolveSurfaceStyle(styleDictionary, run.StyleSlot);
                        PutTextIntoFooterCells(
                            cells,
                            column,
                            run.Text,
                            CreateFooterCellStyle(style));
                        column += TerminalCellWidth.Measure(run.Text);
                    }
                }
            }

            if (!string.IsNullOrEmpty(row.Viewport.Text)) {
                foreach (PopupStyledRun run in BuildPopupStyledRuns(row.Viewport.Text, row.Viewport.StyleSlots, new ushort[row.Viewport.Text.Length])) {
                    if (string.IsNullOrEmpty(run.Text)) {
                        continue;
                    }

                    var style = ResolveSurfaceStyle(styleDictionary, run.StyleSlot);
                    PutTextIntoFooterCells(
                        cells,
                        column,
                        run.Text,
                        CreateFooterCellStyle(style));
                    column += TerminalCellWidth.Measure(run.Text);
                }
            }

            if (!string.IsNullOrEmpty(row.BadgeText)) {
                PutTextIntoFooterCells(
                    cells,
                    column,
                    row.BadgeText,
                    row.BadgeStyleSlots.Length == 0
                        ? FooterCellStyle.Reset
                        : ResolveInlineTextStyle(styleDictionary, row.BadgeText, row.BadgeStyleSlots));
            }
        }

        private void ApplyPopupOverlayCells(
            FooterCompositeCell[] cells,
            PopupOverlayRow row,
            StyleDictionary styleDictionary) {
            int column = row.Column;
            foreach (PopupStyledRun run in BuildPopupStyledRuns(row.Text, row.StyleSlots, row.BackgroundStyleSlots)) {
                if (string.IsNullOrEmpty(run.Text)) {
                    continue;
                }

                var style = ResolvePopupRunStyle(styleDictionary, run.StyleSlot, run.BackgroundStyleSlot);
                PutTextIntoFooterCells(
                    cells,
                    column,
                    run.Text,
                    CreateFooterCellStyle(style));
                column += TerminalCellWidth.Measure(run.Text);
            }
        }

        private static FooterCompositeCell[] CreateFooterCompositeCells(int writableWidth, FooterCellStyle defaultStyle) {
            FooterCompositeCell[] cells = new FooterCompositeCell[Math.Max(0, writableWidth)];
            for (int index = 0; index < cells.Length; index++) {
                cells[index] = new FooterCompositeCell(" ", defaultStyle, GlyphStart: index, GlyphWidth: 1);
            }

            return cells;
        }

        private static void PutTextIntoFooterCells(
            FooterCompositeCell[] cells,
            int startColumn,
            string text,
            FooterCellStyle style) {
            if (cells.Length == 0 || string.IsNullOrEmpty(text)) {
                return;
            }

            int column = Math.Clamp(startColumn, 0, cells.Length);
            int textIndex = 0;
            while (textIndex < text.Length && column < cells.Length) {
                int glyphLength = TerminalCellWidth.TakeLengthByCols(text, textIndex, 1, out int glyphWidth);
                if (glyphLength <= 0) {
                    break;
                }

                if (glyphWidth <= 0) {
                    textIndex += glyphLength;
                    continue;
                }

                if (column + glyphWidth > cells.Length) {
                    break;
                }

                ClearFooterCellSpan(cells, column, glyphWidth);
                string glyphText = text.Substring(textIndex, glyphLength);
                cells[column] = new FooterCompositeCell(glyphText, style, GlyphStart: column, GlyphWidth: glyphWidth);
                for (int offset = 1; offset < glyphWidth; offset++) {
                    cells[column + offset] = new FooterCompositeCell(string.Empty, style, IsContinuation: true, GlyphStart: column, GlyphWidth: glyphWidth);
                }

                column += glyphWidth;
                textIndex += glyphLength;
            }
        }

        private static void ClearFooterCellSpan(FooterCompositeCell[] cells, int startColumn, int width) {
            if (width <= 0) {
                return;
            }

            HashSet<int> clearedStarts = [];
            int endColumn = Math.Min(cells.Length, startColumn + width);
            for (int column = Math.Max(0, startColumn); column < endColumn; column++) {
                int glyphStart = cells[column].GlyphStart;
                if (!clearedStarts.Add(glyphStart)) {
                    continue;
                }

                int glyphWidth = Math.Max(1, cells[glyphStart].GlyphWidth);
                FooterCellStyle defaultStyle = cells[glyphStart].DefaultStyle;
                for (int glyphColumn = glyphStart; glyphColumn < Math.Min(cells.Length, glyphStart + glyphWidth); glyphColumn++) {
                    cells[glyphColumn] = new FooterCompositeCell(
                        glyphColumn == glyphStart ? " " : string.Empty,
                        defaultStyle,
                        IsContinuation: glyphColumn != glyphStart,
                        GlyphStart: glyphStart,
                        GlyphWidth: glyphWidth,
                        DefaultStyleValue: defaultStyle);
                }
            }
        }

        private static string ComposeVirtualFooterCells(FooterCompositeCell[] cells) {
            StringBuilder builder = new();
            FooterCellStyle? activeStyle = null;
            for (int index = 0; index < cells.Length; index++) {
                FooterCompositeCell cell = cells[index];
                if (cell.IsContinuation) {
                    continue;
                }

                if (!Equals(activeStyle, cell.Style)) {
                    builder.Append(AnsiColorCodec.Reset);
                    if (!cell.Style.IsReset) {
                        builder.Append(cell.Style.Ansi);
                    }

                    activeStyle = cell.Style;
                }

                builder.Append(cell.Text);
            }

            builder.Append(AnsiColorCodec.Reset);
            return builder.ToString();
        }

        private void WriteConsoleFooterCells(FooterCompositeCell[] cells) {
            StringBuilder segment = new();
            FooterCellStyle? activeStyle = null;
            for (int index = 0; index < cells.Length; index++) {
                FooterCompositeCell cell = cells[index];
                if (cell.IsContinuation) {
                    continue;
                }

                if (!Equals(activeStyle, cell.Style)) {
                    FlushConsoleFooterSegment(segment, activeStyle);
                    activeStyle = cell.Style;
                }

                segment.Append(cell.Text);
            }

            FlushConsoleFooterSegment(segment, activeStyle);
        }

        private void FlushConsoleFooterSegment(StringBuilder segment, FooterCellStyle? style) {
            if (segment.Length == 0) {
                return;
            }

            string text = segment.ToString();
            segment.Clear();
            if (style is not { } resolvedStyle || resolvedStyle.IsReset) {
                terminalDevice.Write(text);
                return;
            }

            WriteWithConsoleColors(
                text,
                resolvedStyle.Foreground ?? terminalDevice.ForegroundColor,
                resolvedStyle.Background ?? terminalDevice.BackgroundColor);
        }

        private static FooterCellStyle CreateFooterCellStyle(StyledTextStyle? style) {
            if (style is null
                || (style.Foreground is null
                    && style.Background is null
                    && style.TextAttributes == StyledTextAttributes.None)) {
                return FooterCellStyle.Reset;
            }

            return new FooterCellStyle(
                ConsoleTerminalAppearance.FormatAnsi(style.Foreground, style.Background, style.TextAttributes),
                style.Foreground is { } foreground ? ConsoleTerminalAppearance.ResolveConsoleColor(foreground) : null,
                style.Background is { } value ? ConsoleTerminalAppearance.ResolveConsoleColor(value) : null);
        }

        private static FooterCellStyle ResolveStatusLineStyle(StyleDictionary styleDictionary, string? lineStyleId) {
            return CreateFooterCellStyle(ResolveSurfaceStyle(styleDictionary, lineStyleId));
        }

        private static FooterCellStyle ResolveStatusRunStyle(
            StyleDictionary styleDictionary,
            string? lineStyleId,
            string? runStyleId) {
            var lineStyle = ResolveSurfaceStyle(styleDictionary, lineStyleId);
            var runStyle = ResolveSurfaceStyle(styleDictionary, runStyleId);
            var foreground = runStyle?.Foreground ?? lineStyle?.Foreground;
            var background = runStyle?.Background ?? lineStyle?.Background;
            return foreground is null
                ? FooterCellStyle.Reset
                : CreateFooterCellStyle(new StyledTextStyle {
                    StyleId = runStyle?.StyleId ?? lineStyle?.StyleId ?? string.Empty,
                    Foreground = foreground,
                    Background = background,
                    TextAttributes = runStyle?.TextAttributes ?? lineStyle?.TextAttributes ?? StyledTextAttributes.None,
                });
        }

        private static FooterCellStyle ResolveInlineTextStyle(
            StyleDictionary styleDictionary,
            string text,
            ushort[] styleSlots) {
            if (string.IsNullOrEmpty(text)) {
                return FooterCellStyle.Reset;
            }

            var styleSlot = TerminalTextLayoutOps.NormalizePopupStyleSlots(text, styleSlots)
                .FirstOrDefault(static candidate => candidate != 0);
            return CreateFooterCellStyle(ResolveSurfaceStyle(styleDictionary, styleSlot));
        }

        private InputBlockLayout BuildInputLayout(
            TerminalRenderInput input,
            int writableWidth,
            bool supportsVirtualTerminal) {
            string safePrompt = AnsiSanitizer.SanitizeEscapes(input.PromptText);
            ushort[] promptStyleSlots = TerminalTextLayoutOps.NormalizePopupStyleSlots(safePrompt, input.PromptStyleSlots);
            string safeIndicator = AnsiSanitizer.SanitizeEscapes(input.IndicatorText);
            ushort[] indicatorStyleSlots = TerminalTextLayoutOps.NormalizePopupStyleSlots(safeIndicator, input.IndicatorStyleSlots);
            string prefixSeed = string.Concat(safeIndicator, safePrompt);
            ushort[] prefixSeedStyleSlots = prefixSeed.Length == 0
                ? []
                : safeIndicator.Length == 0
                    ? promptStyleSlots
                    : safePrompt.Length == 0
                        ? indicatorStyleSlots
                        : [.. indicatorStyleSlots, .. promptStyleSlots];
            int promptWidth = TerminalCellWidth.Measure(prefixSeed);
            var lines = SplitDisplayLines(input.DisplayText, input.DisplayStyleSlots, input.CursorCompositeIndex);
            var cursorRowIndex = Array.FindIndex(lines, static line => line.HasCursor);
            if (cursorRowIndex < 0) {
                cursorRowIndex = Math.Max(0, lines.Length - 1);
            }

            var rows = new InputRenderRow[lines.Length];
            var cursorColumn = 0;
            for (int index = 0; index < lines.Length; index++) {
                var line = lines[index];
                var prefixText = index == 0 ? prefixSeed : new string(' ', promptWidth);
                var prefixStyleSlots = index == 0 ? prefixSeedStyleSlots : [];
                var prefixWidth = promptWidth;
                var inputWidth = Math.Max(1, writableWidth - prefixWidth);
                var viewport = BuildVisibleInput(
                    line.Text,
                    line.StyleSlots,
                    line.CursorIndex,
                    inputWidth);
                var occupiedWidth = prefixWidth + TerminalCellWidth.Measure(viewport.Text);
                string? badgeText = null;

                if (input.ShowCompletionBadge
                    && index == cursorRowIndex
                    && input.CompletionCount > 0) {
                    var candidateBadge = $" [{input.CompletionIndex}/{input.CompletionCount}]";
                    if (occupiedWidth + TerminalCellWidth.Measure(candidateBadge) <= writableWidth) {
                        badgeText = candidateBadge;
                        occupiedWidth += TerminalCellWidth.Measure(candidateBadge);
                    }
                }

                if (!supportsVirtualTerminal && occupiedWidth < writableWidth) {
                    occupiedWidth = writableWidth;
                }

                if (index == cursorRowIndex) {
                    cursorColumn = Math.Clamp(prefixWidth + viewport.CursorOffset, 0, writableWidth);
                }

                rows[index] = new InputRenderRow(
                    prefixText,
                    prefixStyleSlots,
                    viewport,
                    badgeText,
                    badgeText is null ? [] : TerminalTextLayoutOps.NormalizePopupStyleSlots(badgeText, input.CompletionBadgeStyleSlots));
            }

            return new InputBlockLayout(
                rows,
                cursorRowIndex,
                cursorColumn);
        }

        private static PopupOverlaySnapshot BuildPopupOverlaySnapshot(
            PanePopupMaterial material,
            InputBlockLayout inputLayout,
            int footerTopRow,
            int statusLineCount,
            int footerRowCount,
            int writableWidth) {
            int popupWidth = Math.Clamp(material.Width, 1, Math.Max(1, writableWidth));
            int popupHeight = Math.Max(0, material.Height);
            if (popupHeight == 0) {
                return new PopupOverlaySnapshot([]);
            }

            int horizontalViewportMargin = ResolvePopupHorizontalViewportMargin(
                material.HorizontalViewportMargin,
                writableWidth,
                popupWidth);
            int footerBottomRow = footerTopRow + Math.Max(0, footerRowCount - 1);
            int caretRow = footerTopRow + statusLineCount + inputLayout.CursorRowIndex;
            int preferredBelowTop = caretRow + 1;
            int preferredAboveTop = caretRow - popupHeight;
            int topRow;
            if (preferredBelowTop + popupHeight - 1 <= footerBottomRow) {
                topRow = preferredBelowTop;
            }
            else if (preferredAboveTop >= footerTopRow) {
                topRow = preferredAboveTop;
            }
            else {
                int maxTop = Math.Max(footerTopRow, footerBottomRow - popupHeight + 1);
                topRow = Math.Clamp(preferredBelowTop, footerTopRow, maxTop);
            }

            int maxLeft = Math.Max(
                horizontalViewportMargin,
                writableWidth - horizontalViewportMargin - popupWidth);
            int left = Math.Clamp(inputLayout.CursorColumn, horizontalViewportMargin, maxLeft);
            List<PopupOverlayRow> rows = [];
            PopupCompletionPanel? completionPanel = material.CompletionPanel;
            if (!material.HasSignature && completionPanel is null) {
                return new PopupOverlaySnapshot([]);
            }

            if (material.HasSignature) {
                AppendPopupBoxRows(
                    rows,
                    topRow,
                    left,
                    material.SignatureLines,
                    material.SignatureBoxInnerWidth,
                    material.BorderStyleSlot);
            }

            int completionTop = topRow + material.CompletionSectionTopOffset;
            if (completionPanel is not null) {
                AppendPopupBoxRows(
                    rows,
                    completionTop,
                    left,
                    completionPanel.ListLines,
                    completionPanel.ListInnerWidth,
                    material.BorderStyleSlot);
            }

            if (completionPanel is { DetailLines.Length: > 0, DetailInnerWidth: > 0 } detailPanel) {
                int detailLeft = left + detailPanel.ListInnerWidth + 1;
                int detailTop = completionTop + detailPanel.DetailTopOffset;
                AppendPopupBoxRows(
                    rows,
                    detailTop,
                    detailLeft,
                    detailPanel.DetailLines,
                    detailPanel.DetailInnerWidth,
                    material.BorderStyleSlot);
                JoinPopupDrawerBorder(
                    rows,
                    detailLeft,
                    detailTop,
                    detailTop + detailPanel.DetailLines.Length + 1,
                    completionTop,
                    completionTop + detailPanel.ListLines.Length + 1);
            }

            if (material.HasSignature && completionPanel is not null) {
                int sharedRight = left + completionPanel.ListInnerWidth + 1;
                if (completionPanel.DetailLines.Length > 0 && completionPanel.DetailInnerWidth > 0 && completionPanel.DetailTopOffset == 0) {
                    sharedRight = left + completionPanel.Width - 1;
                }

                JoinPopupStackedBorder(
                    rows,
                    left,
                    completionTop,
                    sharedRight,
                    left + material.SignatureBoxInnerWidth + 1);
            }

            return new PopupOverlaySnapshot([.. rows]);
        }

        private static int ResolvePopupHorizontalViewportMargin(int requestedMargin, int writableWidth, int popupWidth) {
            return Math.Clamp(
                Math.Max(0, requestedMargin),
                0,
                Math.Max(0, (Math.Max(0, writableWidth) - Math.Max(1, popupWidth)) / 2));
        }

        private static string BuildPopupBorderRow(int width, bool isTop) {
            if (width <= 0) {
                return string.Empty;
            }

            if (width == 1) {
                return isTop ? PopupBorderTopLeft.ToString() : PopupBorderBottomLeft.ToString();
            }

            char left = isTop ? PopupBorderTopLeft : PopupBorderBottomLeft;
            char right = isTop ? PopupBorderTopRight : PopupBorderBottomRight;
            return left + new string(PopupBorderHorizontal, Math.Max(0, width - 2)) + right;
        }

        private static void JoinPopupStackedBorder(
            List<PopupOverlayRow> rows,
            int leftColumn,
            int sharedRow,
            int lowerRightColumn,
            int upperRightColumn) {
            ReplacePopupBorderGlyph(
                rows,
                leftColumn,
                sharedRow,
                PopupBorderRightTee);
            ReplacePopupBorderGlyph(
                rows,
                lowerRightColumn,
                sharedRow,
                lowerRightColumn >= upperRightColumn ? PopupBorderLeftTee : PopupBorderTopTee);
        }

        private static void JoinPopupDrawerBorder(
            List<PopupOverlayRow> rows,
            int sharedColumn,
            int detailTopRow,
            int detailBottomRow,
            int listTopRow,
            int listBottomRow) {
            ReplacePopupBorderGlyph(
                rows,
                sharedColumn,
                detailTopRow,
                detailTopRow <= listTopRow ? PopupBorderTopTee : PopupBorderRightTee);
            ReplacePopupBorderGlyph(
                rows,
                sharedColumn,
                detailBottomRow,
                detailBottomRow >= listBottomRow ? PopupBorderBottomTee : PopupBorderRightTee);
        }

        private static void ReplacePopupBorderGlyph(
            List<PopupOverlayRow> rows,
            int column,
            int row,
            char replacement) {
            for (int index = rows.Count - 1; index >= 0; index--) {
                if (rows[index].Row != row || rows[index].Column > column || rows[index].Column + rows[index].Text.Length <= column) {
                    continue;
                }

                int textIndex = column - rows[index].Column;
                char[] characters = rows[index].Text.ToCharArray();
                characters[textIndex] = replacement;
                rows[index] = rows[index] with {
                    Text = new string(characters),
                };
                return;
            }
        }

        private static void AppendPopupBoxRows(
            List<PopupOverlayRow> rows,
            int topRow,
            int left,
            PopupContentLine[] lines,
            int innerWidth,
            ushort borderStyleSlot) {
            if (lines.Length == 0 || innerWidth <= 0) {
                return;
            }

            int width = innerWidth + 2;
            rows.Add(new PopupOverlayRow(
                topRow,
                left,
                BuildPopupBorderRow(width, isTop: true),
                Enumerable.Repeat(borderStyleSlot, width).ToArray(),
                new ushort[width]));
            int rowOffset = 1;
            for (int index = 0; index < lines.Length; index++) {
                PopupContentLine line = TerminalTextLayoutOps.PadPopupContentLine(TerminalTextLayoutOps.FitPopupContentLine(lines[index], innerWidth), innerWidth);
                string rowText = $"{PopupBorderVertical}{line.Text}{PopupBorderVertical}";
                ushort[] rowStyles = new ushort[rowText.Length];
                ushort[] rowBackgroundStyles = new ushort[rowText.Length];
                if (rowStyles.Length > 0) {
                    rowStyles[0] = borderStyleSlot;
                    rowStyles[^1] = borderStyleSlot;
                }
                if (line.StyleSlots.Length > 0) {
                    Array.Copy(line.StyleSlots, 0, rowStyles, 1, Math.Min(line.StyleSlots.Length, Math.Max(0, rowStyles.Length - 2)));
                }
                if (line.BackgroundStyleSlots.Length > 0) {
                    Array.Copy(line.BackgroundStyleSlots, 0, rowBackgroundStyles, 1, Math.Min(line.BackgroundStyleSlots.Length, Math.Max(0, rowBackgroundStyles.Length - 2)));
                }

                rows.Add(new PopupOverlayRow(
                    topRow + rowOffset,
                    left,
                    rowText,
                    rowStyles,
                    rowBackgroundStyles));
                rowOffset += 1;
            }

            rows.Add(new PopupOverlayRow(
                topRow + rowOffset,
                left,
                BuildPopupBorderRow(width, isTop: false),
                Enumerable.Repeat(borderStyleSlot, width).ToArray(),
                new ushort[width]));
        }

        private static PopupStyledRun[] BuildPopupStyledRuns(string text, ushort[] styleSlots, ushort[] backgroundStyleSlots) {
            if (string.IsNullOrEmpty(text)) {
                return [];
            }

            ushort[] normalized = TerminalTextLayoutOps.NormalizePopupStyleSlots(text, styleSlots);
            ushort[] normalizedBackgrounds = TerminalTextLayoutOps.NormalizePopupStyleSlots(text, backgroundStyleSlots);
            List<PopupStyledRun> runs = [];
            ushort activeStyleSlot = normalized[0];
            ushort activeBackgroundStyleSlot = normalizedBackgrounds[0];
            int runStart = 0;
            for (int index = 1; index < text.Length; index++) {
                if (normalized[index] == activeStyleSlot
                    && normalizedBackgrounds[index] == activeBackgroundStyleSlot) {
                    continue;
                }

                runs.Add(new PopupStyledRun(
                    text[runStart..index],
                    activeStyleSlot,
                    activeBackgroundStyleSlot));
                runStart = index;
                activeStyleSlot = normalized[index];
                activeBackgroundStyleSlot = normalizedBackgrounds[index];
            }

            runs.Add(new PopupStyledRun(
                text[runStart..],
                activeStyleSlot,
                activeBackgroundStyleSlot));
            return [.. runs];
        }

        private static StyledTextStyle? ResolvePopupRunStyle(
            StyleDictionary styleDictionary,
            ushort styleSlot,
            ushort backgroundStyleSlot) {
            var baseStyle = ResolveSurfaceStyle(styleDictionary, styleSlot);
            if (baseStyle is null) {
                return null;
            }

            var backgroundStyle = ResolveSurfaceStyle(styleDictionary, backgroundStyleSlot);
            return backgroundStyle?.Background is null
                ? baseStyle
                : new StyledTextStyle {
                    StyleId = baseStyle.StyleId,
                    Slot = baseStyle.Slot,
                    Foreground = baseStyle.Foreground,
                    Background = backgroundStyle.Background,
                    TextAttributes = baseStyle.TextAttributes,
                };
        }

        private static StyledTextStyle? ResolveSurfaceStyle(StyleDictionary styleDictionary, string? styleId) {
            return StyleDictionaryOps.Resolve(styleDictionary, styleId);
        }

        private static StyledTextStyle? ResolveSurfaceStyle(StyleDictionary styleDictionary, ushort styleSlot) {
            return StyleDictionaryOps.Resolve(styleDictionary, styleSlot);
        }

        private void WriteWithConsoleColors(string text, ConsoleColor foreground, ConsoleColor background) {
            ConsoleColor previousForeground = terminalDevice.ForegroundColor;
            ConsoleColor previousBackground = terminalDevice.BackgroundColor;
            try {
                terminalDevice.ForegroundColor = foreground;
                terminalDevice.BackgroundColor = background;
                terminalDevice.Write(text);
            }
            finally {
                terminalDevice.ForegroundColor = previousForeground;
                terminalDevice.BackgroundColor = previousBackground;
            }
        }

        private int GetCursorTopSafe(int fallback) {
            try {
                return terminalDevice.Cursor.Top;
            }
            catch {
                return fallback;
            }
        }

        private void SetCursorPositionSafe(int left, int top) {
            try {
                TerminalViewport viewport = terminalDevice.Viewport;
                terminalDevice.SetCursorPosition(viewport.ClampColumn(left), viewport.ClampRow(top));
            }
            catch {
            }
        }

        private void SetCursorVisibleSafe(bool visible) {
            try {
                terminalDevice.SetCursorVisible(visible);
            }
            catch {
            }
        }

        private FooterLayout ResolveCurrentFooterLayout() {
            if (!footerDrawn || footerTopRow < 0 || footerSnapshot is null || footerSnapshot.RowCount == 0) {
                return default;
            }

            TerminalViewport viewport = terminalDevice.Viewport;
            int boundedAnchorLineIndex = Math.Clamp(
                footerSnapshot.AnchorLineIndex,
                0,
                footerSnapshot.RowCount - 1);
            // Footer rows are explicitly positioned one row at a time via SetCursorPosition.
            // During inverse layout we still treat each logical footer line as exactly one physical
            // row. The renderer owns explicit row-to-row positioning; if we "replayed" terminal
            // wrapping here after a width shrink, we would double-count reflow, push TopRow too far
            // upward, and let clear passes eat pre-footer history.
            int rowsBeforeAnchor = boundedAnchorLineIndex;

            int fallbackCursorTop = viewport.ClampRow(footerTopRow + rowsBeforeAnchor);
            int currentCursorTop = GetCursorTopSafe(fallbackCursorTop);
            int topRow = currentCursorTop - rowsBeforeAnchor;
            int physicalRowCount = footerSnapshot.RowCount;

            int previousTopRow = footerTopRow;
            footerTopRow = viewport.ClampRow(topRow);
            bool viewportChanged = footerSnapshot.DrawnWritableWidth != viewport.WritableWidth
                || footerSnapshot.DrawnWrapWidth != viewport.WrapWidth;
            if (viewportChanged || previousTopRow != footerTopRow) {
#if UNIFIER_TERMINAL_DEBUG_TRACE
                TerminalDebugTrace.WriteThrottled(
                    "FooterLayout.Resolve",
                    $"prevTop={previousTopRow} resolvedTop={footerTopRow} rowsBefore={rowsBeforeAnchor} cursorTop={currentCursorTop} fallbackCursorTop={fallbackCursorTop} physicalRows={physicalRowCount} anchorLine={boundedAnchorLineIndex}/{footerSnapshot.RowCount - 1} anchorColumn={footerSnapshot.AnchorColumn} widths={footerSnapshot.DrawnWritableWidth}/{footerSnapshot.DrawnWrapWidth}->{viewport.WritableWidth}/{viewport.WrapWidth} mode=logical-fixed-row",
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

        private static VisibleInputViewport BuildVisibleInput(
            string composite,
            ushort[] styleSlots,
            int cursorCompositeIndex,
            int maxWidth) {
            if (maxWidth <= 0) {
                return new VisibleInputViewport(string.Empty, [], 0);
            }

            ushort[] normalizedStyleSlots = TerminalTextLayoutOps.NormalizePopupStyleSlots(composite, styleSlots);
            int cursorOffset = TerminalCellWidth.MeasurePrefix(composite, Math.Clamp(cursorCompositeIndex, 0, composite.Length));
            int compositeWidth = TerminalCellWidth.Measure(composite);

            if (compositeWidth <= maxWidth) {
                return new VisibleInputViewport(
                    composite,
                    normalizedStyleSlots,
                    cursorOffset);
            }

            int maxStart = Math.Max(0, compositeWidth - maxWidth);
            int targetStart = Math.Clamp(cursorOffset - maxWidth + 1, 0, maxStart);
            int windowStart = TerminalCellWidth.FindIndexByCols(composite, targetStart, out _);
            int windowLength = TerminalCellWidth.TakeLengthByCols(composite, windowStart, maxWidth, out _);

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
            ushort[] visibleStyleSlots = normalizedStyleSlots.Skip(windowStart).Take(visible.Length).ToArray();
            int cursorVisibleLength = Math.Clamp(cursorCompositeIndex - windowStart, 0, visible.Length);
            int cursorVisibleOffset = TerminalCellWidth.Measure(visible.AsSpan(0, cursorVisibleLength));

            return new VisibleInputViewport(
                visible,
                visibleStyleSlots,
                cursorVisibleOffset);
        }

        private static DisplayLine[] SplitDisplayLines(string text, ushort[] styleSlots, int cursorCompositeIndex) {
            if (string.IsNullOrEmpty(text)) {
                return [new DisplayLine(string.Empty, [], 0, true)];
            }

            List<DisplayLine> lines = [];
            ushort[] normalizedStyleSlots = TerminalTextLayoutOps.NormalizePopupStyleSlots(text, styleSlots);
            int cursorIndex = Math.Clamp(cursorCompositeIndex, 0, text.Length);
            int lineStart = 0;

            while (true) {
                int contentEnd = lineStart;
                while (contentEnd < text.Length && text[contentEnd] != '\r' && text[contentEnd] != '\n') {
                    contentEnd += 1;
                }

                int separatorEnd = contentEnd;
                if (separatorEnd < text.Length) {
                    if (text[separatorEnd] == '\r' && separatorEnd + 1 < text.Length && text[separatorEnd + 1] == '\n') {
                        separatorEnd += 2;
                    }
                    else {
                        separatorEnd += 1;
                    }
                }

                bool hasCursor = cursorIndex >= lineStart
                    && (cursorIndex <= contentEnd || (cursorIndex > contentEnd && cursorIndex < separatorEnd));
                int cursorLineIndex = hasCursor
                    ? Math.Clamp(cursorIndex, lineStart, contentEnd) - lineStart
                    : 0;
                lines.Add(new DisplayLine(
                    contentEnd > lineStart ? text[lineStart..contentEnd] : string.Empty,
                    contentEnd > lineStart ? normalizedStyleSlots[lineStart..contentEnd] : [],
                    cursorLineIndex,
                    hasCursor));
                if (separatorEnd >= text.Length) {
                    if (separatorEnd > contentEnd) {
                        lines.Add(new DisplayLine(
                            string.Empty,
                            [],
                            0,
                            cursorIndex == text.Length));
                    }

                    break;
                }

                lineStart = separatorEnd;
            }

            return [.. lines];
        }

        private sealed record FooterSnapshot(
            int[] RenderedRowWidths,
            int RowCount,
            int AnchorLineIndex,
            int AnchorColumn,
            int DrawnWritableWidth,
            int DrawnWrapWidth);

        private sealed record PopupOverlaySnapshot(PopupOverlayRow[] Rows);
        private readonly record struct PopupOverlayRow(
            int Row,
            int Column,
            string Text,
            ushort[] StyleSlots,
            ushort[] BackgroundStyleSlots);

        private readonly record struct PopupStyledRun(
            string Text,
            ushort StyleSlot,
            ushort BackgroundStyleSlot);
        private readonly record struct FooterCellStyle(
            string Ansi,
            ConsoleColor? Foreground = null,
            ConsoleColor? Background = null)
        {
            public static FooterCellStyle Reset { get; } = new(AnsiColorCodec.Reset);
            public bool IsReset => string.Equals(Ansi, AnsiColorCodec.Reset, StringComparison.Ordinal)
                && Foreground is null
                && Background is null;
        }

        private sealed record FooterCompositeCell(
            string Text,
            FooterCellStyle Style,
            bool IsContinuation = false,
            int GlyphStart = 0,
            int GlyphWidth = 1,
            FooterCellStyle? DefaultStyleValue = null)
        {
            public FooterCellStyle DefaultStyle { get; init; } = DefaultStyleValue ?? Style;
        }

        private readonly record struct FooterLayout(int TopRow, int PhysicalRowCount)
        {
            public bool IsValid => PhysicalRowCount > 0;
        }

        private readonly record struct DisplayLine(
            string Text,
            ushort[] StyleSlots,
            int CursorIndex,
            bool HasCursor);
        private readonly record struct FooterRowSnapshot(
            StatusRenderRow? Status,
            InputRenderRow? Input);
        private readonly record struct StatusRenderRow(
            string RenderText,
            StyledTextRun[] StyledRuns,
            string LineStyleId)
        {
            public bool IsStyled => StyledRuns.Length > 0;
        }
        private readonly record struct InputRenderRow(
            string PrefixText,
            ushort[] PrefixStyleSlots,
            VisibleInputViewport Viewport,
            string? BadgeText,
            ushort[] BadgeStyleSlots);
        private readonly record struct InputBlockLayout(
            InputRenderRow[] Rows,
            int CursorRowIndex,
            int CursorColumn)
        {
            public int PhysicalRowCount => Rows.Length;
        }

        private readonly record struct VisibleInputViewport(
            string Text,
            ushort[] StyleSlots,
            int CursorOffset);
    }
}
