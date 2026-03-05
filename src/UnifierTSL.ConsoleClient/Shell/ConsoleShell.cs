namespace UnifierTSL.ConsoleClient.Shell
{
    public sealed class ConsoleShell : IInteractiveFrontend
    {
        private readonly object sync = new();
        private readonly LineEditorSession lineEditorSession = new();
        private readonly TerminalCapabilities terminalCapabilities;
        private readonly TerminalRenderer renderer = new();

        private bool disposed;
        private bool readInProgress;
        private int statusScrollOffset;
        private bool hasOutputCursor;
        private bool outputLineContinuation;
        private int outputCursorLeft;
        private int outputCursorTop;

        private ReadLineRenderSnapshot currentContext = ReadLineRenderSnapshot.CreatePlain();
        private LineEditorTransientStatus? transientStatus;

        public ConsoleShell()
        {
            terminalCapabilities = TerminalCapabilities.Detect();
            lineEditorSession.SetSuggestionProvider(ResolveSuggestions);
        }

        public ReadLineViewModel BuildViewModel(ReadLineRenderSnapshot render, ReadLineReactiveState state)
        {
            ArgumentNullException.ThrowIfNull(render);
            ArgumentNullException.ThrowIfNull(state);

            string input = state.InputText ?? string.Empty;
            IReadOnlyList<string> suggestions = ResolveSuggestions(render, input);
            List<string> statusLines = BuildDisplayStatusLines(render, transientStatus: null);

            List<ReadLineCandidateView> candidates = suggestions.Select((value, index) => new ReadLineCandidateView {
                Value = value,
                Weight = 0,
                IsSelected = state.CompletionCount > 0 && state.CompletionIndex > 0 && index == state.CompletionIndex - 1,
            }).ToList();

            return new ReadLineViewModel {
                Purpose = render.Payload.Purpose,
                Prompt = render.Payload.Prompt,
                InputText = input,
                GhostText = string.Empty,
                CursorIndex = state.CursorIndex,
                CompletionIndex = state.CompletionIndex,
                CompletionCount = state.CompletionCount,
                StatusLines = statusLines.Select((text, index) => new ReadLineStatusView {
                    Text = text,
                    IsHeader = index == 0 && text.StartsWith("STATUS", StringComparison.OrdinalIgnoreCase),
                }).ToList(),
                Candidates = candidates,
            };
        }

        public bool IsInteractive => terminalCapabilities.IsInteractive;

        public bool SupportsVirtualTerminal => terminalCapabilities.SupportsVirtualTerminal;

        public void AppendLog(string text, bool isAnsi)
        {
            if (string.IsNullOrEmpty(text)) {
                return;
            }

            lock (sync) {
                ThrowIfDisposed();

                if (IsInteractive && renderer.FooterDrawn) {
                    renderer.ClearFooterArea();
                    if (hasOutputCursor) {
                        int clearedTop = ClampRow(Console.CursorTop);
                        if (outputCursorTop > clearedTop) {
                            // Footer reservation near buffer bottom can trigger an implicit scroll.
                            // Keep output anchor at/above the cleared row to avoid downward jumps
                            // and phantom blank lines.
                            outputCursorTop = clearedTop;
                        }
                    }
                }

                if (IsInteractive) {
                    MoveToOutputCursorIfNeeded();
                }

                string output = WriteText(text, isAnsi);
                CaptureOutputCursor(output);

                if (IsInteractive && (readInProgress || transientStatus is not null)) {
                    int? anchorTopRow = hasOutputCursor ? ResolveFooterAnchorRow() : null;
                    DrawFooter(anchorTopRow);
                }
            }
        }

        public void SetTransientStatus(
            string summary,
            IReadOnlyList<string>? detailLines = null,
            string spinner = "|",
            int panelHeight = 3)
        {
            lock (sync) {
                ThrowIfDisposed();

                string normalizedSpinner = string.IsNullOrWhiteSpace(spinner) ? "|" : spinner;
                string normalizedSummary = string.IsNullOrWhiteSpace(summary) ? "busy" : summary;
                List<string> normalizedDetails = detailLines?
                    .Where(static line => !string.IsNullOrWhiteSpace(line))
                    .ToList() ?? [];

                transientStatus = new LineEditorTransientStatus(
                    Spinner: normalizedSpinner,
                    Summary: normalizedSummary,
                    DetailLines: normalizedDetails,
                    PanelHeight: Math.Clamp(panelHeight, 1, 8));

                if (!IsInteractive) {
                    return;
                }

                if (renderer.FooterDrawn) {
                    renderer.ClearFooterArea();
                }

                DrawFooter();
            }
        }

        public void ClearTransientStatus()
        {
            lock (sync) {
                ThrowIfDisposed();
                if (transientStatus is null) {
                    return;
                }

                transientStatus = null;
                if (!IsInteractive) {
                    return;
                }

                if (!renderer.FooterDrawn) {
                    return;
                }

                if (readInProgress) {
                    renderer.ClearFooterArea();
                    DrawFooter();
                }
                else {
                    renderer.ClearFooterArea();
                }
            }
        }

        public void Clear()
        {
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
            ReadLineRenderSnapshot? render = null,
            bool trim = false,
            CancellationToken cancellationToken = default,
            Action<ReadLineReactiveState>? onInputStateChanged = null)
        {
            render ??= ReadLineRenderSnapshot.CreatePlain();

            if (!IsInteractive) {
                string fallbackLine = ReadLineFallback(render, cancellationToken);
                return trim ? fallbackLine.Trim() : fallbackLine;
            }

            lock (sync) {
                ThrowIfDisposed();
                readInProgress = true;
                currentContext = render;
                statusScrollOffset = 0;
                lineEditorSession.BeginNewLine(render.Payload.GhostText, render.Payload.EnableCtrlEnterBypassGhostFallback);
                DrawFooter();
                NotifyInputStateChanged(onInputStateChanged);
            }

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
                            DrawFooter();
                            NotifyInputStateChanged(onInputStateChanged);
                            break;

                        case LineEditorInputActionKind.ScrollStatus:
                            UpdateStatusScroll(action.Delta);
                            DrawFooter();
                            break;

                        case LineEditorInputActionKind.Cancel:
                            readInProgress = false;
                            renderer.ClearFooterArea();
                            currentContext = ReadLineRenderSnapshot.CreatePlain();
                            return string.Empty;

                        case LineEditorInputActionKind.Submit:
                            string line = action.Payload ?? string.Empty;
                            line = ResolveSubmitLine(render, line, action.ForceRawSubmit);

                            readInProgress = false;
                            renderer.ClearFooterArea();
                            currentContext = ReadLineRenderSnapshot.CreatePlain();
                            return trim ? line.Trim() : line;
                    }
                }
            }
        }

        public void UpdateReadLineContext(ReadLineRenderSnapshot render)
        {
            lock (sync) {
                ThrowIfDisposed();
                currentContext = render ?? ReadLineRenderSnapshot.CreatePlain();

                if (!readInProgress || !IsInteractive) {
                    return;
                }

                if (currentContext.Paging.Enabled) {
                    lineEditorSession.SyncPagedCompletionWindow(currentContext.Paging.SelectedWindowIndex);
                }

                if (renderer.FooterDrawn) {
                    renderer.ClearFooterArea();
                }
                DrawFooter();
            }
        }

        public void Dispose()
        {
            lock (sync) {
                if (disposed) {
                    return;
                }

                disposed = true;
                readInProgress = false;
                renderer.Reset();
            }
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
        }

        private string ReadLineFallback(ReadLineRenderSnapshot render, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            List<string> fallbackStatusLines = BuildDisplayStatusLines(render, transientStatus: null);
            if (fallbackStatusLines.Count > 0) {
                foreach (string statusLine in fallbackStatusLines) {
                    string output = render.Payload.AllowAnsiStatusEscapes && !SupportsVirtualTerminal
                        ? AnsiSanitizer.StripAnsi(statusLine)
                        : statusLine;
                    Console.WriteLine($"[status] {output}");
                }
            }

            string prompt = string.IsNullOrEmpty(render.Payload.Prompt) ? "> " : render.Payload.Prompt;
            Console.Write(prompt);
            string line = Console.ReadLine() ?? string.Empty;
            line = ResolveSubmitLine(render, line, forceRawSubmit: false);
            return line;
        }

        private static string ResolveSubmitLine(ReadLineRenderSnapshot render, string input, bool forceRawSubmit)
        {
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

        private string WriteText(string text, bool isAnsi)
        {
            string output = isAnsi ? text : AnsiSanitizer.SanitizeEscapes(text);
            if (!SupportsVirtualTerminal) {
                output = AnsiSanitizer.StripAnsi(output);
            }

            Console.Write(output);
            return output;
        }

        private void DrawFooter(int? anchorTopRow = null)
        {
            LineEditorRenderState inputState = lineEditorSession.GetRenderState();
            StatusViewport viewport = BuildStatusViewport(currentContext);
            // Normalize once here so all downstream row math uses the same bounded coordinate space.
            int? normalizedAnchor = anchorTopRow is int requestedAnchor
                ? ClampRow(requestedAnchor)
                : null;
            renderer.Draw(
                currentContext,
                inputState,
                viewport.Lines,
                readInProgress,
                SupportsVirtualTerminal,
                normalizedAnchor);

            if (normalizedAnchor is int anchorRow && hasOutputCursor) {
                int actualFooterTop = renderer.FooterTopRow;
                int footerShift = anchorRow - actualFooterTop;
                if (footerShift > 0) {
                    // If footer reservation triggered a scroll, actual footer top is higher than requested.
                    // We intentionally mirror that shift onto outputCursorTop so future writes stay glued
                    // to the real visible output line.
                    outputCursorTop = ClampRow(outputCursorTop - footerShift);
                }
            }
        }

        private StatusViewport BuildStatusViewport(ReadLineRenderSnapshot render)
        {
            List<string> details = BuildDisplayStatusLines(render, transientStatus);
            int panelHeight = Math.Clamp(render.Payload.StatusPanelHeight, 1, 8);
            if (transientStatus is LineEditorTransientStatus transient) {
                panelHeight = Math.Clamp(Math.Max(panelHeight, transient.PanelHeight), 1, 8);
            }

            int totalLines = 1 + details.Count;
            int maxScroll = Math.Max(0, totalLines - panelHeight);
            statusScrollOffset = Math.Clamp(statusScrollOffset, 0, maxScroll);

            int startLine = Math.Min(totalLines, statusScrollOffset + 1);
            int endLine = Math.Min(totalLines, statusScrollOffset + panelHeight);
            string summary = transientStatus is LineEditorTransientStatus transientSummary
                ? $"STATUS {transientSummary.Spinner} purpose:{render.Payload.Purpose} task:{AnsiSanitizer.SanitizeEscapes(transientSummary.Summary)} view:{startLine}-{endLine}/{totalLines}"
                : $"STATUS purpose:{render.Payload.Purpose} view:{startLine}-{endLine}/{totalLines}";

            List<string> allLines = [summary, .. details];
            List<string> visible = allLines
                .Skip(statusScrollOffset)
                .Take(panelHeight)
                .ToList();

            while (visible.Count < panelHeight) {
                visible.Add(string.Empty);
            }

            return new StatusViewport(visible);
        }

        private static List<string> BuildDisplayStatusLines(ReadLineRenderSnapshot render, LineEditorTransientStatus? transientStatus)
        {
            List<string> lines = [];
            foreach (string statusLine in render.Payload.StatusLines.Where(static s => !string.IsNullOrWhiteSpace(s))) {
                lines.Add(render.Payload.AllowAnsiStatusEscapes
                    ? statusLine
                    : AnsiSanitizer.SanitizeEscapes(statusLine));
            }

            if (transientStatus is LineEditorTransientStatus transient) {
                lines.Add("work: " + AnsiSanitizer.SanitizeEscapes(transient.Summary));
                foreach (string detail in transient.DetailLines) {
                    if (string.IsNullOrWhiteSpace(detail)) {
                        continue;
                    }

                    lines.Add(AnsiSanitizer.SanitizeEscapes(detail));
                }
            }

            return lines;
        }

        private void NotifyInputStateChanged(Action<ReadLineReactiveState>? onInputStateChanged)
        {
            if (onInputStateChanged is null) {
                return;
            }

            try {
                LineEditorRenderState inputState = lineEditorSession.GetRenderState();
                ReadLineReactiveState state = new() {
                    Purpose = currentContext.Payload.Purpose,
                    InputText = inputState.Text ?? string.Empty,
                    CursorIndex = inputState.CursorIndex,
                    CompletionIndex = inputState.CompletionIndex,
                    CompletionCount = inputState.CompletionCount,
                    CandidateWindowOffset = currentContext.Paging.WindowOffset,
                };
                onInputStateChanged(state);
            }
            catch {
            }
        }

        private void UpdateStatusScroll(int delta)
        {
            if (delta == 0) {
                return;
            }

            List<string> lines = BuildDisplayStatusLines(currentContext, transientStatus);
            int panelHeight = Math.Clamp(currentContext.Payload.StatusPanelHeight, 1, 8);
            int totalLines = 1 + lines.Count;
            int maxScroll = Math.Max(0, totalLines - panelHeight);
            statusScrollOffset = Math.Clamp(statusScrollOffset + delta, 0, maxScroll);
        }

        private IReadOnlyList<string> ResolveSuggestions(string input)
        {
            return ResolveSuggestions(currentContext, input);
        }

        private static IReadOnlyList<string> ResolveSuggestions(ReadLineRenderSnapshot render, string input)
        {
            List<string> orderedValues = [];
            if (!string.IsNullOrWhiteSpace(render.Payload.GhostText)) {
                orderedValues.Add(render.Payload.GhostText);
            }

            orderedValues.AddRange(render.Payload.Candidates
                .Where(static item => item is not null && !string.IsNullOrWhiteSpace(item.Value))
                .Select(static item => item.Value));

            IReadOnlyList<string> ordered = DistinctPreserveOrder(orderedValues);
            if (string.IsNullOrEmpty(input)) {
                return ordered;
            }

            if (render.Paging.Enabled) {
                return ordered;
            }

            return ordered
                .Where(candidate => candidate.StartsWith(input, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        private static IReadOnlyList<string> DistinctPreserveOrder(IEnumerable<string> values)
        {
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            List<string> output = [];

            foreach (string value in values) {
                if (string.IsNullOrWhiteSpace(value)) {
                    continue;
                }

                if (!seen.Add(value)) {
                    continue;
                }

                output.Add(value);
            }

            return output;
        }

        private void MoveToOutputCursorIfNeeded()
        {
            if (!hasOutputCursor) {
                return;
            }

            try {
                int safeLeft = ClampColumn(outputCursorLeft);
                int safeTop = ClampRow(outputCursorTop);
                int currentTop = ClampRow(Console.CursorTop);
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

        private void CaptureOutputCursor(string output)
        {
            if (string.IsNullOrEmpty(output) || !IsInteractive) {
                return;
            }

            try {
                outputCursorLeft = ClampColumn(Console.CursorLeft);
                outputCursorTop = ClampRow(Console.CursorTop);
                hasOutputCursor = true;
                outputLineContinuation = !EndsWithLineTerminator(output);
            }
            catch {
                hasOutputCursor = false;
                outputLineContinuation = false;
            }
        }

        private int ResolveFooterAnchorRow()
        {
            int anchorRow = outputCursorTop + (outputLineContinuation ? 1 : 0);
            return ClampRow(anchorRow);
        }

        private static bool EndsWithLineTerminator(string output)
        {
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

        private readonly record struct StatusViewport(IReadOnlyList<string> Lines);
    }
}
