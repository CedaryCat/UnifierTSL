using System.Runtime.InteropServices;
using UnifierTSL.ConsoleClient.Shared.ConsolePrompting;

namespace UnifierTSL.ConsoleClient.Shell
{
    public sealed partial class ConsoleShell : IDisposable
    {
        private readonly object sync = new();
        private readonly LineEditorSession lineEditorSession = new();
        private readonly TerminalCapabilities terminalCapabilities;
        private readonly TerminalRenderer renderer = new();
        private readonly Timer statusAnimationTimer;
        private readonly int virtualCaretBlinkIntervalMs = ResolveVirtualCaretBlinkIntervalMs();

        private bool disposed;
        private bool readInProgress;
        private bool nativeBlinkSuppressed;
        private int statusScrollOffset;
        private bool hasOutputCursor;
        private bool outputLineContinuation;
        private int outputCursorLeft;
        private int outputCursorTop;
        private bool virtualCaretVisible = true;
        private long virtualCaretAnchorTick = Environment.TickCount64;
        private int lastViewportWritableWidth = -1;
        private int lastViewportWrapWidth = -1;
        private long viewportLastChangedTick = Environment.TickCount64;

        private ConsoleRenderSnapshot currentContext = ConsoleRenderSnapshot.CreatePlain();
        private ConsolePromptTheme activeTheme = ConsolePromptTheme.Default;
        private StatusBarSurface? statusBar;
        private const int MaxStatusBodyLines = 5;
        private const int FooterViewportStabilizationMs = 180;

        public ConsoleShell() {
            terminalCapabilities = TerminalCapabilities.Detect();
            lineEditorSession.SetSuggestionProvider(ResolveSuggestions);
            statusAnimationTimer = new Timer(
                static state => ((ConsoleShell)state!).TickStatusAnimation(),
                this,
                50,
                50);
        }

        public bool IsInteractive => terminalCapabilities.IsInteractive;

        public bool SupportsVirtualTerminal => terminalCapabilities.SupportsVirtualTerminal;

        public void UpdateTheme(ConsolePromptTheme theme) {
            lock (sync) {
                ThrowIfDisposed();
                activeTheme = (theme ?? ConsolePromptTheme.Default) with { };

                if (!IsInteractive || !renderer.FooterDrawn) {
                    return;
                }

                DrawFooter();
            }
        }

        public void AppendLog(string text, bool isAnsi) {
            if (string.IsNullOrEmpty(text)) {
                return;
            }

            lock (sync) {
                ThrowIfDisposed();

                if (IsInteractive && renderer.FooterDrawn) {
                    renderer.ClearFooterArea(SupportsVirtualTerminal);
                    if (hasOutputCursor) {
                        int clearedTop = TerminalRenderer.ClampRow(Console.CursorTop);
                        if (!outputLineContinuation) {
                            // When previous output ended with a line terminator, the next write should
                            // always restart from the cleared footer top row at column 0. This prevents
                            // stale cached anchors from drifting into old footer rows.
                            outputCursorTop = clearedTop;
                            outputCursorLeft = 0;
                        }
                        else if (outputCursorTop > clearedTop) {
                            // Footer reservation near buffer bottom can trigger an implicit scroll.
                            // Keep output anchor at/above the cleared row to avoid downward jumps
                            // and phantom blank lines.
                            outputCursorTop = clearedTop;
                        }
                    }
                }

                if (IsInteractive) {
                    MoveToOutputCursorIfNeeded();
                    ClearOutputRowForFreshLineWrite();
                }

                string output = WriteText(text, isAnsi);
                CaptureOutputCursor(output);

                if (IsInteractive && (readInProgress || statusBar is not null)) {
                    int? anchorTopRow = hasOutputCursor ? ResolveFooterAnchorRow() : null;
                    DrawFooter(anchorTopRow);
                }
            }
        }

        public void UpdateStatusFrame(
            long sequence,
            string text,
            int indicatorFrameIntervalMs = 0,
            string indicatorStylePrefix = "",
            string indicatorFrames = "") {
            lock (sync) {
                ThrowIfDisposed();

                long nowTick = Environment.TickCount64;
                string normalizedText = text ?? string.Empty;
                string normalizedStylePrefix = indicatorStylePrefix ?? string.Empty;
                string normalizedIndicatorFrames = indicatorFrames ?? string.Empty;
                string[] decodedIndicatorFrames = ConsoleStatusIndicatorFramesCodec.Deserialize(normalizedIndicatorFrames);
                int normalizedFrameIntervalMs = Math.Max(0, indicatorFrameIntervalMs);

                if (decodedIndicatorFrames.Length == 0 || normalizedFrameIntervalMs <= 0) {
                    normalizedFrameIntervalMs = 0;
                }

                long frameAnchorTick = nowTick;
                if (statusBar is StatusBarSurface previous
                    && previous.FrameIntervalMs == normalizedFrameIntervalMs
                    && string.Equals(previous.StylePrefix, normalizedStylePrefix, StringComparison.Ordinal)
                    && previous.Frames.AsSpan().SequenceEqual(decodedIndicatorFrames)) {
                    frameAnchorTick = previous.FrameAnchorTick;
                }

                statusBar = new StatusBarSurface(
                    sequence: sequence,
                    text: normalizedText,
                    frameIntervalMs: normalizedFrameIntervalMs,
                    stylePrefix: normalizedStylePrefix,
                    frames: decodedIndicatorFrames,
                    frameAnchorTick: frameAnchorTick);
                RefreshRenderedIndicatorLocked(statusBar, nowTick);

                if (!IsInteractive) {
                    return;
                }

                DrawFooter();
            }
        }

        public void ClearStatusFrame() {
            lock (sync) {
                ThrowIfDisposed();
                if (statusBar is null) {
                    return;
                }

                statusBar = null;
                if (!IsInteractive) {
                    return;
                }

                if (!renderer.FooterDrawn) {
                    return;
                }

                if (readInProgress) {
                    DrawFooter();
                }
                else {
                    renderer.ClearFooterArea(SupportsVirtualTerminal);
                    // Footer-only teardown should leave output at a clean fresh-line anchor.
                    // Without resetting this state, the next log line may reuse a stale cursor
                    // snapshot and leave right-side status remnants.
                    outputCursorTop = TerminalRenderer.ClampRow(Console.CursorTop);
                    outputCursorLeft = 0;
                    outputLineContinuation = false;
                    hasOutputCursor = true;
                }
            }
        }

        public void Clear() {
            lock (sync) {
                ThrowIfDisposed();
                Console.Clear();
                renderer.Reset();
                hasOutputCursor = false;
                outputLineContinuation = false;
                outputCursorLeft = 0;
                outputCursorTop = 0;
            }
        }

        public string ReadLine(
            ConsoleRenderSnapshot? render = null,
            bool trim = false,
            CancellationToken cancellationToken = default,
            Action<ConsoleInputState>? onInputStateChanged = null) {
            render ??= ConsoleRenderSnapshot.CreatePlain();

            if (!IsInteractive) {
                string fallbackLine = ReadLineFallback(render, cancellationToken);
                return trim ? fallbackLine.Trim() : fallbackLine;
            }

            lock (sync) {
                ThrowIfDisposed();
                readInProgress = true;
                SetNativeBlinkSuppressedLocked(true);
                currentContext = render;
                statusScrollOffset = 0;
                ResetVirtualCaretBlinkLocked();
                lineEditorSession.BeginNewLine(render.Payload.GhostText, render.Payload.EnableCtrlEnterBypassGhostFallback);
                if (render.Paging.Enabled) {
                    lineEditorSession.SyncPagedCompletionWindow(render.Paging);
                }
                DrawFooter();
                NotifyInputStateChanged(onInputStateChanged);
            }

            try {
                while (true) {
                    cancellationToken.ThrowIfCancellationRequested();

                    ConsoleKeyInfo keyInfo;
                    try {
                        keyInfo = Console.ReadKey(intercept: true);
                    }
                    catch (Exception) {
                        string fallbackLine = ReadLineFallback(render, cancellationToken);
                        return trim ? fallbackLine.Trim() : fallbackLine;
                    }

                    lock (sync) {
                        LineEditorInputAction action = lineEditorSession.ApplyKey(keyInfo);

                        switch (action.Kind) {
                            case LineEditorInputActionKind.None:
                                break;

                            case LineEditorInputActionKind.Redraw:
                            case LineEditorInputActionKind.Autocomplete:
                                ResetVirtualCaretBlinkLocked();
                                DrawFooter();
                                NotifyInputStateChanged(onInputStateChanged);
                                break;

                            case LineEditorInputActionKind.ScrollStatus:
                                UpdateStatusScroll(action.Delta);
                                ResetVirtualCaretBlinkLocked();
                                DrawFooter();
                                break;

                            case LineEditorInputActionKind.Cancel:
                                readInProgress = false;
                                renderer.ClearFooterArea(SupportsVirtualTerminal);
                                currentContext = ConsoleRenderSnapshot.CreatePlain();
                                if (statusBar is not null) {
                                    DrawFooter();
                                }
                                return string.Empty;

                            case LineEditorInputActionKind.Submit:
                                string line = action.Payload ?? string.Empty;
                                line = ResolveSubmitLine(render, line, action.ForceRawSubmit);

                                readInProgress = false;
                                renderer.ClearFooterArea(SupportsVirtualTerminal);
                                currentContext = ConsoleRenderSnapshot.CreatePlain();
                                if (statusBar is not null) {
                                    DrawFooter();
                                }
                                return trim ? line.Trim() : line;
                        }
                    }
                }
            }
            finally {
                lock (sync) {
                    SetNativeBlinkSuppressedLocked(false);
                }
            }
        }

        public void UpdateReadLineContext(ConsoleRenderSnapshot render) {
            lock (sync) {
                ThrowIfDisposed();
                currentContext = render ?? ConsoleRenderSnapshot.CreatePlain();

                if (!readInProgress || !IsInteractive) {
                    return;
                }

                if (currentContext.Paging.Enabled) {
                    lineEditorSession.SyncPagedCompletionWindow(currentContext.Paging);
                }

                DrawFooter();
            }
        }

        public void Dispose() {
            bool disposeTimer = false;
            lock (sync) {
                if (disposed) {
                    return;
                }

                disposed = true;
                readInProgress = false;
                SetNativeBlinkSuppressedLocked(false);
                renderer.Reset();
                disposeTimer = true;
            }

            if (!disposeTimer) {
                return;
            }

            try {
                statusAnimationTimer.Dispose();
            }
            catch {
            }
        }

        private void ThrowIfDisposed() {
            ObjectDisposedException.ThrowIf(disposed, this);
        }

        private string ReadLineFallback(ConsoleRenderSnapshot render, CancellationToken cancellationToken) {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            if (HasStatusContent(render)) {
                StatusFrame frame = BuildStatusFrame(render, 0);
                foreach (string statusLine in frame.Lines) {
                    string output = SupportsVirtualTerminal
                        ? statusLine
                        : AnsiSanitizer.StripAnsi(statusLine);
                    Console.WriteLine($"[status] {output}");
                }
            }

            string prompt = string.IsNullOrEmpty(render.Payload.Prompt) ? "> " : render.Payload.Prompt;
            Console.Write(prompt);
            string line = Console.ReadLine() ?? string.Empty;
            line = ResolveSubmitLine(render, line, forceRawSubmit: false);
            return line;
        }

        private static string ResolveSubmitLine(ConsoleRenderSnapshot render, string input, bool forceRawSubmit) {
            if (forceRawSubmit) {
                return input;
            }

            if (render.Payload.EmptySubmitBehavior != EmptySubmitBehavior.AcceptGhostIfAvailable) {
                return input;
            }

            if (!string.IsNullOrWhiteSpace(input)) {
                return input;
            }

            if (string.IsNullOrWhiteSpace(render.Payload.GhostText)) {
                return input;
            }

            return render.Payload.GhostText;
        }

        private string WriteText(string text, bool isAnsi) {
            string output = isAnsi ? text : AnsiSanitizer.SanitizeEscapes(text);
            if (!SupportsVirtualTerminal) {
                output = AnsiSanitizer.StripAnsi(output);
            }

            Console.Write(output);
            return output;
        }

        private void DrawFooter(int? anchorTopRow = null) {
            if (ShouldDeferFooterRenderLocked()) {
                return;
            }

            LineEditorRenderState inputState = lineEditorSession.GetRenderState();
            StatusFrame frame = BuildStatusFrame(currentContext, statusScrollOffset);
            statusScrollOffset = frame.ScrollOffset;
            // Normalize once here so all downstream row math uses the same bounded coordinate space.
            int? normalizedAnchor = anchorTopRow is int requestedAnchor
                ? TerminalRenderer.ClampRow(requestedAnchor)
                : null;
            renderer.Draw(
                currentContext,
                inputState,
                frame.Lines,
                readInProgress,
                readInProgress && virtualCaretVisible,
                SupportsVirtualTerminal,
                normalizedAnchor,
                frame.HeaderTheme);

            if (normalizedAnchor is int anchorRow && hasOutputCursor) {
                int actualFooterTop = renderer.FooterTopRow;
                int footerShift = anchorRow - actualFooterTop;
                if (footerShift != 0) {
#if UNIFIER_TERMINAL_DEBUG_TRACE
                    TerminalDebugTrace.WriteThrottled(
                        "ConsoleShell.FooterShift",
                        $"anchor={anchorRow} actualTop={actualFooterTop} shift={footerShift} outputTopBefore={outputCursorTop} outputLeft={outputCursorLeft} continuation={outputLineContinuation} readInProgress={readInProgress} statusLines={frame.Lines.Count}",
                        minIntervalMs: 80);
#endif
                }

                if (footerShift > 0) {
                    outputCursorTop = TerminalRenderer.ClampRow(outputCursorTop - footerShift);
                }
            }
        }

        private bool ShouldDeferFooterRenderLocked() {
            // Console hosts report width changes before row reflow/cursor relocation fully settle.
            // Clearing the old footer immediately is safe, but redrawing against that transient
            // geometry is not: the new footer anchor can be computed from pre-reflow rows and then
            // applied after reflow, which makes the footer drift or fold into log output while the
            // user is still dragging the window edge.
            int currentWritableWidth = TerminalRenderer.GetWritableWidth();
            int currentWrapWidth = TerminalRenderer.GetWrapWidth();
            long nowTick = Environment.TickCount64;

            if (lastViewportWritableWidth < 0 || lastViewportWrapWidth < 0) {
                lastViewportWritableWidth = currentWritableWidth;
                lastViewportWrapWidth = currentWrapWidth;
                viewportLastChangedTick = nowTick;
                return false;
            }

            if (lastViewportWritableWidth != currentWritableWidth
                || lastViewportWrapWidth != currentWrapWidth) {
                int cursorTop;
                try {
                    cursorTop = TerminalRenderer.ClampRow(Console.CursorTop);
                }
                catch {
                    cursorTop = -1;
                }

#if UNIFIER_TERMINAL_DEBUG_TRACE
                TerminalDebugTrace.WriteThrottled(
                    "ConsoleShell.ViewportChange",
                    $"viewport={lastViewportWritableWidth}/{lastViewportWrapWidth}->{currentWritableWidth}/{currentWrapWidth} cursorTop={cursorTop} readInProgress={readInProgress}",
                    minIntervalMs: 80);
#endif
                if (renderer.FooterDrawn) {
                    // Hide the stale footer immediately so terminal host reflow during drag
                    // cannot visually fold the old footer into unrelated rows before redraw stabilizes.
                    renderer.ClearFooterArea(SupportsVirtualTerminal);
                }
                lastViewportWritableWidth = currentWritableWidth;
                lastViewportWrapWidth = currentWrapWidth;
                viewportLastChangedTick = nowTick;
                return true;
            }

            return nowTick - viewportLastChangedTick < FooterViewportStabilizationMs;
        }

        private StatusFrame BuildStatusFrame(ConsoleRenderSnapshot render, int requestedScrollOffset) {
            List<string> bodyLines = [.. render.Payload.StatusBodyLines
                .Where(static line => !string.IsNullOrWhiteSpace(line))];
            int totalBodyLines = bodyLines.Count;
            int maxScroll = Math.Max(0, totalBodyLines - MaxStatusBodyLines);
            int scrollOffset = Math.Clamp(requestedScrollOffset, 0, maxScroll);
            List<string> visibleBodyLines = bodyLines
                .Skip(scrollOffset)
                .Take(MaxStatusBodyLines)
                .ToList();

            int startLine = totalBodyLines == 0 ? 0 : scrollOffset + 1;
            int endLine = totalBodyLines == 0 ? 0 : scrollOffset + visibleBodyLines.Count;
            string headerLine = FormatStatusHeaderLine(
                render.Payload.InputSummary ?? string.Empty,
                statusBar,
                startLine, endLine, totalBodyLines);
            List<string> lines = [headerLine, .. visibleBodyLines];
            return new StatusFrame(lines, scrollOffset, activeTheme);
        }

        private static string FormatStatusHeaderLine(
            string inputSummary,
            StatusBarSurface? statusBarSurface,
            int startLine,
            int endLine,
            int totalBodyLines) {
            bool hasSummary = !string.IsNullOrWhiteSpace(inputSummary);
            bool hasStatusBar = statusBarSurface is not null
                && !string.IsNullOrWhiteSpace(statusBarSurface.Text);

            string core;
            if (!hasStatusBar && !hasSummary) {
                core = "STATUS";
            }
            else if (!hasStatusBar) {
                core = $"STATUS {inputSummary}";
            }
            else {
                string indicator = statusBarSurface!.RenderedIndicator ?? string.Empty;
                string indicatorPart = string.IsNullOrWhiteSpace(indicator)
                    ? string.Empty
                    : string.IsNullOrWhiteSpace(statusBarSurface.StylePrefix)
                        ? $" [{indicator}]"
                        : $" {statusBarSurface.StylePrefix}[{indicator}]{AnsiColorCodec.Reset}";
                core = $"STATUS{indicatorPart} {statusBarSurface.Text}";
                if (hasSummary) {
                    core += $" | {inputSummary}";
                }
            }

            string resetBoundary = AnsiSanitizer.ContainsEscape(core)
                ? AnsiColorCodec.Reset
                : string.Empty;
            return $"{core}{resetBoundary}[{startLine}~{endLine}/{totalBodyLines}]";
        }

        private void NotifyInputStateChanged(Action<ConsoleInputState>? onInputStateChanged) {
            if (onInputStateChanged is null) {
                return;
            }

            try {
                ConsoleInputState state = lineEditorSession.BuildInputState(currentContext.Payload.Purpose);
                onInputStateChanged(state);
            }
            catch {
            }
        }

        private void TickStatusAnimation() {
            lock (sync) {
                bool hasFooterTarget = readInProgress || statusBar is not null;
                if (disposed || !IsInteractive || (!renderer.FooterDrawn && !hasFooterTarget)) {
                    return;
                }

                long nowTick = Environment.TickCount64;
                bool shouldRedraw = !renderer.FooterDrawn && hasFooterTarget;
                if (renderer.FooterDrawn && renderer.RequiresViewportRefresh()) {
                    shouldRedraw = true;
                }

                if (statusBar is not null && RefreshRenderedIndicatorLocked(statusBar, nowTick)) {
                    shouldRedraw = true;
                }

                if (readInProgress && RefreshVirtualCaretLocked(nowTick)) {
                    shouldRedraw = true;
                }

                if (!shouldRedraw) {
                    return;
                }

                DrawFooter();
            }
        }

        private bool RefreshVirtualCaretLocked(long nowTick) {
            bool visible = ResolveVirtualCaretVisible(nowTick);
            if (visible == virtualCaretVisible) {
                return false;
            }

            virtualCaretVisible = visible;
            return true;
        }

        private bool ResolveVirtualCaretVisible(long nowTick) {
            if (virtualCaretBlinkIntervalMs <= 0) {
                return true;
            }

            long elapsed = Math.Max(0, nowTick - virtualCaretAnchorTick);
            long step = elapsed / virtualCaretBlinkIntervalMs;
            return (step & 1) == 0;
        }

        private void ResetVirtualCaretBlinkLocked() {
            virtualCaretAnchorTick = Environment.TickCount64;
            virtualCaretVisible = true;
        }

        private static bool RefreshRenderedIndicatorLocked(StatusBarSurface surface, long nowTick) {
            string rendered = ResolveRenderedIndicator(surface, nowTick, out int step);
            bool changed = step != surface.LastFrameStep
                || !string.Equals(rendered, surface.RenderedIndicator, StringComparison.Ordinal);
            surface.LastFrameStep = step;
            surface.RenderedIndicator = rendered;
            return changed;
        }

        private static string ResolveRenderedIndicator(
            StatusBarSurface surface,
            long nowTick,
            out int step) {
            string[] frames = surface.Frames;
            if (frames.Length == 0) {
                step = 0;
                return string.Empty;
            }

            if (surface.FrameIntervalMs <= 0) {
                step = 0;
                return frames[0];
            }

            long elapsed = Math.Max(0, nowTick - surface.FrameAnchorTick);
            long ticks = elapsed / surface.FrameIntervalMs;
            step = (int)(ticks % frames.Length);
            return frames[step];
        }

        private void UpdateStatusScroll(int delta) {
            if (delta == 0) {
                return;
            }

            int totalBodyLines = currentContext.Payload.StatusBodyLines
                .Count(static line => !string.IsNullOrWhiteSpace(line));
            int maxScroll = Math.Max(0, totalBodyLines - MaxStatusBodyLines);
            statusScrollOffset = Math.Clamp(statusScrollOffset + delta, 0, maxScroll);
        }

        private bool HasStatusContent(ConsoleRenderSnapshot render) {
            if (statusBar is not null) {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(render.Payload.InputSummary)) {
                return true;
            }

            return render.Payload.StatusBodyLines.Any(static line => !string.IsNullOrWhiteSpace(line));
        }

        private IReadOnlyList<string> ResolveSuggestions(string input) {
            return ResolveSuggestions(currentContext, input);
        }

        private static IReadOnlyList<string> ResolveSuggestions(ConsoleRenderSnapshot render, string input) {
            if (render.Paging.Enabled) {
                return ConsoleTextSetOps.DistinctPreserveOrder(render.Payload.Candidates
                    .Where(static item => item is not null && !string.IsNullOrWhiteSpace(item.Value))
                    .Select(static item => item.Value));
            }

            List<string> orderedValues = [];
            if (!string.IsNullOrWhiteSpace(render.Payload.GhostText)) {
                orderedValues.Add(render.Payload.GhostText);
            }

            orderedValues.AddRange(render.Payload.Candidates
                .Where(static item => item is not null && !string.IsNullOrWhiteSpace(item.Value))
                .Select(static item => item.Value));

            IReadOnlyList<string> ordered = ConsoleTextSetOps.DistinctPreserveOrder(orderedValues);
            if (string.IsNullOrEmpty(input)) {
                return ordered;
            }

            return ordered
                .Where(candidate => candidate.StartsWith(input, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        private void MoveToOutputCursorIfNeeded() {
            if (!hasOutputCursor) {
                return;
            }

            try {
                int safeLeft = TerminalRenderer.ClampColumn(outputCursorLeft);
                int safeTop = TerminalRenderer.ClampRow(outputCursorTop);
                int currentTop = TerminalRenderer.ClampRow(Console.CursorTop);
                if (safeTop > currentTop) {
                    // After clear/scroll, cached anchor can be stale and lower than current visible position.
                    // Clamp to currentTop to avoid jumping downward into empty-looking space.
                    safeTop = currentTop;
                }
                if (Console.CursorLeft != safeLeft || Console.CursorTop != safeTop) {
                    Console.SetCursorPosition(safeLeft, safeTop);
                }

                outputCursorLeft = safeLeft;
                outputCursorTop = safeTop;
            }
            catch {
                hasOutputCursor = false;
                outputLineContinuation = false;
            }
        }

        private void CaptureOutputCursor(string output) {
            if (string.IsNullOrEmpty(output) || !IsInteractive) {
                return;
            }

            try {
                int left = TerminalRenderer.ClampColumn(Console.CursorLeft);
                int top = TerminalRenderer.ClampRow(Console.CursorTop);
                outputCursorLeft = left;
                outputCursorTop = top;
                hasOutputCursor = true;
                bool endedWithLineTerminator = EndsWithLineTerminator(output);
                outputLineContinuation = left != 0 && !endedWithLineTerminator;
            }
            catch {
                hasOutputCursor = false;
                outputLineContinuation = false;
            }
        }

        private void ClearOutputRowForFreshLineWrite() {
            if (outputLineContinuation) {
                return;
            }

            try {
                int row = TerminalRenderer.ClampRow(Console.CursorTop);
                Console.SetCursorPosition(0, row);
                if (SupportsVirtualTerminal) {
                    Console.Write(AnsiColorCodec.Reset);
                    Console.Write("\u001b[2K\r");
                }
                else {
                    int width = TerminalRenderer.GetWritableWidth();
                    Console.Write(new string(' ', width));
                    Console.SetCursorPosition(0, row);
                }

                outputCursorTop = row;
                outputCursorLeft = 0;
                hasOutputCursor = true;
            }
            catch {
            }
        }

        private int ResolveFooterAnchorRow() {
            int anchorRow = outputCursorTop + (outputLineContinuation ? 1 : 0);
            return TerminalRenderer.ClampRow(anchorRow);
        }

        private static bool EndsWithLineTerminator(string output) {
            // Many log lines end with ANSI reset after CRLF.
            // Reading raw last char ("m") would wrongly treat the line as unterminated.
            // We strip ANSI first, then check the real text ending.
            string normalized = AnsiSanitizer.StripAnsi(output);
            if (normalized.Length == 0) {
                return false;
            }

            char lastChar = normalized[^1];
            return lastChar == '\n' || lastChar == '\r';
        }

        private void SetNativeBlinkSuppressedLocked(bool suppress) {
            if (!IsInteractive || !SupportsVirtualTerminal || nativeBlinkSuppressed == suppress) {
                return;
            }

            try {
                Console.Write(suppress ? "\u001b[?12l" : "\u001b[?12h");
                nativeBlinkSuppressed = suppress;
            }
            catch {
            }
        }

        private static int ResolveVirtualCaretBlinkIntervalMs() {
            if (!OperatingSystem.IsWindows()) {
                return 530;
            }

            try {
                uint blinkInterval = GetCaretBlinkTime();
                if (blinkInterval == uint.MaxValue) {
                    return 0;
                }

                if (blinkInterval == 0) {
                    return 530;
                }

                return (int)Math.Min(blinkInterval, int.MaxValue);
            }
            catch {
                return 530;
            }
        }

        [LibraryImport("user32.dll")]
        private static partial uint GetCaretBlinkTime();


        private sealed class StatusBarSurface(
            long sequence,
            string text,
            int frameIntervalMs,
            string stylePrefix,
            string[] frames,
            long frameAnchorTick)
        {
            public readonly long Sequence = sequence;
            public readonly string Text = text;
            public readonly int FrameIntervalMs = frameIntervalMs;
            public readonly string StylePrefix = stylePrefix;
            public readonly string[] Frames = frames;
            public readonly long FrameAnchorTick = frameAnchorTick;
            public int LastFrameStep = -1;
            public string RenderedIndicator = string.Empty;
        }

        private readonly record struct StatusFrame(IReadOnlyList<string> Lines, int ScrollOffset, ConsolePromptTheme HeaderTheme);
    }
}
