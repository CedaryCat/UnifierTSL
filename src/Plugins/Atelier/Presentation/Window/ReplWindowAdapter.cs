using Atelier.Commanding.Meta;
using Atelier.Presentation.Prompt;
using Atelier.Presentation.Window.Drafting;
using Atelier.Presentation.Window.Highlighting;
using Atelier.Presentation.Window.Transcript;
using Atelier.Session;
using Atelier.Session.Context;
using System.Text;
using System.Threading.Channels;
using UnifierTSL.Surface.Prompting;
using UnifierTSL.Surface.Interactions;
using UnifierTSL.Contracts.Display;
using UnifierTSL.Contracts.Projection;
using UnifierTSL.Surface.Prompting.Model;
using UnifierTSL.Contracts.Protocol.Payloads;
using UnifierTSL.Contracts.Sessions;
using UnifierTSL.Contracts.Terminal;
using UnifierTSL.Surface.Hosting;
using UnifierTSL.TextEditing;

namespace Atelier.Presentation.Window
{
    internal sealed partial class ReplWindowAdapter : IDisposable
    {
        #region State And Lifecycle

        private const string ReplIndentUnit = "    ";
        private const int PromptSpinnerIntervalMs = 120;
        private const string HiddenPrompt = "  ";
        private static readonly TranscriptFormatter TranscriptFormatter = new();
        private static readonly string[] PromptSpinnerFrames = ["| ", "/ ", "- ", "\\ "];
        private static readonly ProjectionTextBlock[] PromptSpinnerFrameBlocks = [.. PromptSpinnerFrames.Select(frame => CreateSingleLineBlock(frame))];
        private static readonly ProjectionTextAnimation PromptIndicatorAnimation = new() {
            FrameStepTicks = 7,
            Frames = PromptSpinnerFrameBlocks,
        };
        private readonly Lock sync = new();
        private readonly Action<ReplWindowAdapter> released;
        private readonly Action<ReplSession> releaseSession;
        private readonly OpenOptions options;
        private readonly ISurfaceSession surfaceSession;
        private readonly ReplSession session;
        private readonly Channel<AdapterCommand> commandQueue = Channel.CreateUnbounded<AdapterCommand>(new UnboundedChannelOptions {
            SingleReader = true,
            SingleWriter = false,
        });
        private readonly CancellationTokenSource processingCancellation = new();
        private readonly SemaphoreSlim authoringSessionGate = new(1, 1);
        private readonly SemaphoreSlim consoleReadGate = new(1, 1);
        private readonly Task processingTask;
        private readonly Queue<char> consoleInputBuffer = [];
        private readonly PromptProjectionBuilder promptProjectionBuilder = new();
        private Task initializationTask = Task.CompletedTask;
        private UnifierTSL.Surface.Interactions.SurfaceInteractionScope? interactionScope;
        private EditorBufferSnapshot latestClientBase = EditorBufferSnapshot.Empty;
        private DraftSnapshot latestClientBaseDraft = DraftSnapshot.Empty;
        private PromptHighlightSpan[] latestClientBaseSourceHighlights = [];
        private readonly RemoteOverlayLedger remoteOverlayLedger = new();
        private ClientBufferedEditorState? latestEditorSyncState;
        private long latestEditorSyncSerial;
        private ClientBufferedEditorState? latestAuthoringState;
        private long latestAuthoringSerial;
        private long editorSyncVersion;
        private int editorSyncScheduled;
        private int authoringUpdateScheduled;
        private int latestRequestedCompletionIndex;
        private string latestRequestedCompletionItemId = string.Empty;
        private string latestRequestedPreferredCompletionText = string.Empty;
        private string latestRequestedPreferredInterpretationId = string.Empty;
        private TaskCompletionSource<string>? pendingConsoleInput;
        private PendingConsoleKeyRead? pendingConsoleKeyRead;
        private bool consoleInteractionActive;
        private readonly HashSet<Task> backgroundTasks = [];
        private bool blockingExecutionActive;
        private EditorSubmitKeyMode submitKeyMode;
        private bool submitKeyModeAutoReset;
        private InteractionScopeMode interactionScopeMode;
        private bool startRequested;
        private bool disposed;

        public ReplWindowAdapter(
            OpenOptions options,
            ISurfaceSession surfaceSession,
            ReplSession session,
            Action<ReplWindowAdapter> released,
            Action<ReplSession> releaseSession) {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.surfaceSession = surfaceSession ?? throw new ArgumentNullException(nameof(surfaceSession));
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            this.released = released ?? throw new ArgumentNullException(nameof(released));
            this.releaseSession = releaseSession ?? throw new ArgumentNullException(nameof(releaseSession));

            this.surfaceSession.PublishSurfaceHostOperation(SurfaceHostOperations.SetTitle("Atelier REPL"));
            var initialPublication = this.session.CurrentPublication;
            var initialText = initialPublication.SourceText ?? string.Empty;
            latestClientBase = new EditorBufferSnapshot(
                Math.Max(0, initialPublication.Revision.ClientBufferRevision),
                0,
                EditorPaneKind.MultiLine,
                initialText,
                Math.Clamp(initialPublication.Revision.CaretIndex, 0, initialText.Length),
                [.. (initialPublication.Revision.Markers ?? [])],
                [],
                []);
            latestClientBaseDraft = ReplPairEngine.DecodeBaseDraft(latestClientBase.ToClientState());
            latestRequestedPreferredCompletionText = initialPublication.Workspace.Completion.PreferredCompletionText;
            this.session.Console.Bind(
                PublishConsoleStreamText,
                PublishConsoleStreamLine,
                ClearTranscript,
                ReadConsoleLine,
                ReadConsoleChar,
                ReadConsoleKey);
            this.session.PublicationChanged += OnSessionPublicationChanged;
            this.surfaceSession.ReleaseRequested += OnReleaseRequested;
            processingTask = Task.Run(ProcessCommandsAsync);
        }

        public void Start() {
            lock (sync) {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (startRequested) {
                    return;
                }

                startRequested = true;
            }

            surfaceSession.Start();
            lock (sync) {
                if (disposed) {
                    return;
                }

                initializationTask = StartAuthoringAsync();
            }
        }

        public void Dispose() {
            Task initializationToAwait;
            lock (sync) {
                if (disposed) {
                    return;
                }

                disposed = true;
                initializationToAwait = initializationTask;
                latestEditorSyncState = null;
                latestEditorSyncSerial = checked(latestEditorSyncSerial + 1);
                latestAuthoringState = null;
                latestAuthoringSerial = checked(latestAuthoringSerial + 1);
            }

            session.PublicationChanged -= OnSessionPublicationChanged;
            surfaceSession.ReleaseRequested -= OnReleaseRequested;
            session.Console.Unbind();
            CancelPendingConsoleReads(new ObjectDisposedException(nameof(ReplWindowAdapter)));

            commandQueue.Writer.TryComplete();
            processingCancellation.Cancel();
            try {
                initializationToAwait.GetAwaiter().GetResult();
            }
            catch {
            }

            try {
                processingTask.GetAwaiter().GetResult();
            }
            catch {
            }

            UnifierTSL.Surface.Interactions.SurfaceInteractionScope? scopeToDispose;
            var scopeMode = InteractionScopeMode.None;
            lock (sync) {
                scopeToDispose = interactionScope;
                scopeMode = interactionScopeMode;
                interactionScope = null;
                interactionScopeMode = InteractionScopeMode.None;
            }

            DetachScopeHandlers(scopeToDispose, scopeMode);
            DisposeScope(scopeToDispose);

            try {
                surfaceSession.Dispose();
            }
            catch {
            }

            processingCancellation.Dispose();
            authoringSessionGate.Dispose();
            consoleReadGate.Dispose();
            releaseSession(session);
            released(this);
        }

        private async Task StartAuthoringAsync() {
            try {
                ShowAuthoringScope(session.CurrentPublication);
            }
            catch (OperationCanceledException) {
                return;
            }
            catch (ObjectDisposedException) {
                return;
            }
            catch (Exception ex) {
                PublishStartupFailure(ex);
                return;
            }

            try {
                var publication = await session.WarmupTask.WaitAsync(processingCancellation.Token).ConfigureAwait(false);
                processingCancellation.Token.ThrowIfCancellationRequested();
                PublishIfCurrent(publication);
            }
            catch (OperationCanceledException) {
            }
            catch (ObjectDisposedException) {
            }
            catch (Exception ex) {
                PublishWarmupFailure(ex);
            }
        }

        #endregion

        #region Command Processing

        private async Task ProcessCommandsAsync() {
            try {
                await foreach (var command in commandQueue.Reader.ReadAllAsync(processingCancellation.Token).ConfigureAwait(false)) {
                    switch (command.Kind) {
                        case AdapterCommandKind.Submit:
                            if (command.BufferedState is not null) {
                                await ProcessSubmitAsync(command.BufferedState, processingCancellation.Token).ConfigureAwait(false);
                            }
                            break;

                        case AdapterCommandKind.Publication:
                            if (command.Publication is not null) {
                                PublishIfCurrent(command.Publication);
                            }
                            break;

                        case AdapterCommandKind.FormatEditor:
                            ProcessFormatEditorCommand();
                            break;
                    }
                }
            }
            catch (OperationCanceledException) when (processingCancellation.IsCancellationRequested) {
            }
            catch (ObjectDisposedException) {
            }
        }

        private void ScheduleAuthoringUpdate(ClientBufferedEditorState bufferedState, long? expectedEditorSyncSerial = null) {
            lock (sync) {
                if (disposed) {
                    return;
                }

                if (expectedEditorSyncSerial is { } expected && expected != latestEditorSyncSerial) {
                    return;
                }

                latestAuthoringState = bufferedState;
                latestAuthoringSerial = checked(latestAuthoringSerial + 1);
            }

            if (Interlocked.CompareExchange(ref authoringUpdateScheduled, 1, 0) == 0) {
                _ = Task.Run(ProcessAuthoringUpdatesAsync, CancellationToken.None);
            }
        }

        private async Task ProcessAuthoringUpdatesAsync() {
            try {
                while (true) {
                    ClientBufferedEditorState? bufferedState;
                    long serial;
                    lock (sync) {
                        if (disposed || latestAuthoringState is null) {
                            Volatile.Write(ref authoringUpdateScheduled, 0);
                            return;
                        }

                        bufferedState = latestAuthoringState;
                        serial = latestAuthoringSerial;
                    }

                    await authoringSessionGate.WaitAsync(processingCancellation.Token).ConfigureAwait(false);
                    try {
                        lock (sync) {
                            if (disposed || serial != latestAuthoringSerial) {
                                continue;
                            }
                        }

                        await ApplyAuthoringStateAsync(bufferedState, processingCancellation.Token).ConfigureAwait(false);
                    }
                    finally {
                        authoringSessionGate.Release();
                    }

                    lock (sync) {
                        if (disposed) {
                            latestAuthoringState = null;
                            Volatile.Write(ref authoringUpdateScheduled, 0);
                            return;
                        }

                        if (serial == latestAuthoringSerial) {
                            latestAuthoringState = null;
                            Volatile.Write(ref authoringUpdateScheduled, 0);
                            return;
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (processingCancellation.IsCancellationRequested) {
                Volatile.Write(ref authoringUpdateScheduled, 0);
            }
            catch (ObjectDisposedException) {
                Volatile.Write(ref authoringUpdateScheduled, 0);
            }
            catch {
                Volatile.Write(ref authoringUpdateScheduled, 0);
            }
        }

        private async Task ApplyAuthoringStateAsync(ClientBufferedEditorState bufferedState, CancellationToken cancellationToken) {
            bufferedState = NormalizePromptBufferedState(bufferedState);
            var sourceClientBufferRevision = Math.Max(0, bufferedState.ClientBufferRevision);
            var update = await session.UpdateDraftAsync(
                    sourceClientBufferRevision,
                    bufferedState.BufferText ?? string.Empty,
                    bufferedState.CaretIndex,
                    bufferedState.Markers,
                    cancellationToken)
                .ConfigureAwait(false);
            if (update.SourceTextChanged) {
                RequestAuthoringAnalysis(cancellationToken);
            }
        }

        private void RequestAuthoringAnalysis(CancellationToken cancellationToken) {
            var task = session.RequestAnalysisAsync(cancellationToken).AsTask();
            _ = task.ContinueWith(
                static completed => _ = completed.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private async Task ProcessSubmitAsync(ClientBufferedEditorState bufferedState, CancellationToken cancellationToken) {
            bufferedState = NormalizePromptBufferedState(bufferedState);
            var sourceClientBufferRevision = Math.Max(0, bufferedState.ClientBufferRevision);
            var submittedBuffer = bufferedState.BufferText ?? string.Empty;
            var submittedDraft = DraftMarkers.Decode(submittedBuffer, bufferedState.Markers, bufferedState.CaretIndex);
            var submittedSourceText = submittedDraft.SourceText;
            var submittedHighlights = ResolveSubmittedInputHighlights(submittedSourceText);
            if (MetaCommandParser.TryParseSubmittedBuffer(submittedSourceText, out var command)) {
                if (command.Kind != MetaCommandKind.Clear) {
                    PublishSubmittedInput(submittedSourceText, submittedHighlights);
                }

                await ProcessMetaCommandAsync(sourceClientBufferRevision, command, cancellationToken).ConfigureAwait(false);
                return;
            }

            await UpdateSubmittedDraftAsync(
                    sourceClientBufferRevision,
                    submittedBuffer,
                    bufferedState.CaretIndex,
                    bufferedState.Markers,
                    cancellationToken)
                .ConfigureAwait(false);
            PublishSubmittedInput(submittedSourceText, submittedHighlights);
            RunResult? runResult = null;
            ResetTemporarySubmitKeyMode();
            EnterBlockingExecutionState();
            try {
                runResult = await session.QueuePersistentSubmitAsync(cancellationToken).ConfigureAwait(false);
                PublishPersistentSubmitResult(runResult);
                TrackBackgroundTask(runResult);
            }
            catch (Exception ex) when (!processingCancellation.IsCancellationRequested) {
                PublishErrorBox("Execution", [ex.Message]);
            }
            finally {
                EndConsoleInteraction();
                ExitBlockingExecutionState();
            }
        }

        private async Task UpdateSubmittedDraftAsync(
            long sourceClientBufferRevision,
            string submittedBuffer,
            int caretIndex,
            IReadOnlyList<ClientBufferedTextMarker>? markers,
            CancellationToken cancellationToken) {
            lock (sync) {
                latestAuthoringState = null;
                latestAuthoringSerial = checked(latestAuthoringSerial + 1);
            }

            await authoringSessionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try {
                await session.UpdateDraftAsync(
                        sourceClientBufferRevision,
                        submittedBuffer,
                        caretIndex,
                        markers,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            finally {
                authoringSessionGate.Release();
            }
        }

        private async Task ProcessMetaCommandAsync(long sourceClientBufferRevision, MetaCommand command, CancellationToken cancellationToken) {
            var draftCleared = false;
            switch (command.Kind) {
                case MetaCommandKind.Help:
                    PublishMetaCommandHelp();
                    break;

                case MetaCommandKind.Reset:
                    var resetPublication = await session.ResetAsync(cancellationToken).ConfigureAwait(false);
                    PublishStreamLine(resetPublication.Invalidation is null
                        ? "Session reset."
                        : resetPublication.Invalidation.Reason);
                    break;

                case MetaCommandKind.Clear:
                    surfaceSession.PublishSurfaceOperation(SurfaceOperations.Stream(StreamPayloadKind.Clear, channel: StreamChannel.Transcript));
                    break;

                case MetaCommandKind.Imports:
                    PublishImportState();
                    break;

                case MetaCommandKind.Target:
                    PublishStreamLine($"Target: {options.TargetProfile.Label}");
                    PublishStreamLine($"Host: {options.InvocationHost.Label}");
                    break;

                case MetaCommandKind.Paste:
                    ProcessPasteModeCommand(command);
                    break;

                case MetaCommandKind.Transient:
                    PublishStreamLine("Running transient code.");
                    RunResult? runResult = null;
                    ResetTemporarySubmitKeyMode();
                    EnterBlockingExecutionState();
                    try {
                        runResult = await session.QueueTransientRunAsync(command.TransientCode, cancellationToken).ConfigureAwait(false);
                        PublishTransientRunResult(runResult);
                        PublishHostClearDraftProposal(sourceClientBufferRevision);
                        TrackBackgroundTask(runResult);
                        draftCleared = true;
                    }
                    catch (Exception ex) when (!processingCancellation.IsCancellationRequested) {
                        PublishErrorBox("Execution", [ex.Message]);
                    }
                    finally {
                        EndConsoleInteraction();
                        ExitBlockingExecutionState();
                    }
                    break;

                default:
                    const string guidanceHint = "Type ':' for command suggestions, or run :help for usage guidance.";
                    PublishStreamLine(string.IsNullOrWhiteSpace(command.Name)
                        ? "Unknown command. " + guidanceHint
                        : $"Unknown command ':{command.Name}'. {guidanceHint}");
                    break;
            }

            if (command.Kind != MetaCommandKind.Reset && !draftCleared) {
                PublishHostClearDraftProposal(sourceClientBufferRevision);
            }
        }

        private void PublishHostClearDraftProposal(long expectedClientBufferRevision) {
            RemoteOverlayProposal? proposal = null;
            SessionPublication? publication = null;
            lock (sync) {
                if (disposed || latestClientBase.Text.Length == 0) {
                    return;
                }

                var clearState = new ClientBufferedEditorState {
                    Kind = latestClientBase.Kind,
                    BufferText = string.Empty,
                    CaretIndex = 0,
                    ClientBufferRevision = Math.Max(0, expectedClientBufferRevision),
                    AcceptedRemoteRevision = latestClientBase.AcceptedRemoteRevision,
                    Markers = [],
                };
                proposal = remoteOverlayLedger.AppendProjection(latestClientBase, clearState, DraftSnapshot.Empty, []);
                publication = session.CurrentPublication;
                editorSyncVersion = checked(editorSyncVersion + 1);
            }

            if (publication is not null && proposal is not null) {
                PublishDraftDocument(
                    publication,
                    proposal.ProjectedState,
                    proposal.ProjectedDraft,
                    proposal.ProjectedSourceHighlights,
                    proposal.RemoteRevision);
            }
        }

        private void PublishMetaCommandHelp() {
            PublishStreamLine("Atelier REPL quick help:");
            PublishStreamLine("  Mental model:");
            PublishStreamLine("    Atelier is a Roslyn C# script session, not a line-by-line shell.");
            PublishStreamLine("    The editor analyzes committed history plus the current draft as one synthetic script.");
            PublishStreamLine("    Normal submissions that change state are committed to session history.");
            PublishStreamLine("    Later drafts see earlier declarations, imports, references, and loaded submissions.");
            PublishStreamLine("    Meta commands start with ':' and are intercepted before C# compilation.");
            PublishStreamLine("    :transient compiles against the current session, runs as a probe, then discards its changes.");
            PublishStreamLine("  Async and cancellation:");
            PublishStreamLine("    Top-level await may keep running in background after the prompt returns.");
            PublishStreamLine("    Background execution Tasks are available as PendingTasks; LastTask is the newest one.");
            PublishStreamLine("    An unfinished Task returned as the result is also tracked by the window indicator.");
            PublishStreamLine("    The session Cancellation token is available as Cancellation.");
            PublishStreamLine("    Cancellation injection mainly prevents runaway loops from surviving a closed session.");
            PublishStreamLine("    Loops check Cancellation automatically, so intentional long-running loops can stop on close.");
            PublishStreamLine("    Token-aware calls may get Cancellation appended automatically when a safe overload exists.");
            PublishStreamLine("    Task.Run/Thread/Timer callbacks are guarded so detached work also observes cancellation.");
            PublishStreamLine("    Blocking APIs that ignore tokens still need explicit cancellation-aware design.");
            PublishStreamLine("  Lifetime:");
            PublishStreamLine("    Closing the REPL releases the surface and session automatically.");
            PublishStreamLine("    Session disposal cancels pending work, clears PendingTasks, and unloads script contexts.");
            PublishStreamLine("    Reset clears committed history and restores baseline imports.");
            PublishStreamLine("  Editing:");
            PublishStreamLine("    Enter follows Roslyn's complete-submission check; unfinished blocks stay open.");
            PublishStreamLine("    Shift+Enter always inserts a line; paste mode uses Ctrl+Enter to submit once.");
            PublishStreamLine("    Diagnostics, completions, and signature help describe the draft in accumulated context.");
            PublishStreamLine("    Pair closers can stay virtual until typed through.");
            PublishStreamLine("    Shift+Tab formats the draft without submitting.");
        }

        private void ProcessPasteModeCommand(MetaCommand command) {
            if (!TryResolvePasteMode(command.HeaderRemainder, out var mode, out var autoReset)) {
                PublishStreamLine("Usage: :paste [on|off]");
                return;
            }

            SetSubmitKeyMode(mode, autoReset);
            PublishStreamLine(mode == EditorSubmitKeyMode.CtrlEnter
                ? "Paste mode enabled for the next submit. Enter inserts new lines; Ctrl+Enter submits."
                : "Paste mode disabled. Enter uses smart submit; Shift+Enter inserts new lines.");
        }

        private bool TryResolvePasteMode(string text, out EditorSubmitKeyMode mode, out bool autoReset) {
            var value = (text ?? string.Empty).Trim();
            if (value.Length == 0) {
                var pasteModeActive = GetSubmitKeyMode() == EditorSubmitKeyMode.CtrlEnter;
                mode = pasteModeActive ? EditorSubmitKeyMode.Enter : EditorSubmitKeyMode.CtrlEnter;
                autoReset = !pasteModeActive;
                return true;
            }

            switch (value.ToLowerInvariant()) {
                case "on":
                case "ctrl":
                case "ctrl-enter":
                case "ctrl+enter":
                    mode = EditorSubmitKeyMode.CtrlEnter;
                    autoReset = true;
                    return true;
                case "off":
                case "enter":
                case "normal":
                    mode = EditorSubmitKeyMode.Enter;
                    autoReset = false;
                    return true;
                default:
                    mode = default;
                    autoReset = false;
                    return false;
            }
        }

        private EditorSubmitKeyMode GetSubmitKeyMode() {
            lock (sync) {
                return submitKeyMode;
            }
        }

        private void SetSubmitKeyMode(EditorSubmitKeyMode mode, bool autoReset) {
            lock (sync) {
                if (!disposed) {
                    submitKeyMode = mode;
                    submitKeyModeAutoReset = autoReset && mode == EditorSubmitKeyMode.CtrlEnter;
                }
            }
        }

        private void ResetTemporarySubmitKeyMode() {
            var changed = false;
            lock (sync) {
                if (submitKeyModeAutoReset) {
                    submitKeyMode = EditorSubmitKeyMode.Enter;
                    submitKeyModeAutoReset = false;
                    changed = true;
                }
            }

            if (changed) {
                PublishStreamLine("Paste mode disabled. Enter uses smart submit; Shift+Enter inserts new lines.");
            }
        }

        private void PublishImportState() {
            var importState = session.ImportState;
            PublishStreamLine($"BaselineImports ({importState.BaselineImports.Length}):");
            PublishValueList(importState.BaselineImports);
            PublishStreamLine($"EffectiveImports ({importState.EffectiveImports.Length}):");
            PublishValueList(importState.EffectiveImports);
            PublishStreamLine($"ReferencePaths ({importState.ReferencePaths.Length}):");
            PublishValueList(importState.ReferencePaths);
        }

        private void PublishValueList(IReadOnlyList<string> values) {
            if (values.Count == 0) {
                PublishStreamLine("  (none)");
                return;
            }

            foreach (var value in values) {
                PublishStreamLine($"  {value}");
            }
        }

        #endregion

        #region Transcript Output

        private void ClearTranscript() {
            try {
                surfaceSession.PublishSurfaceOperation(SurfaceOperations.Stream(StreamPayloadKind.Clear, channel: StreamChannel.Transcript));
            }
            catch (ObjectDisposedException) {
            }
        }

        private void PublishConsoleStreamText(string text, ConsoleColor foreground, ConsoleColor background) {
            try {
                PublishStyledStream(
                    StreamPayloadKind.AppendText,
                    CreateConsoleStyledLine(text, foreground, background),
                    CreateConsoleStyleDictionary(foreground, background));
            }
            catch (ObjectDisposedException) {
            }
        }

        private void PublishStreamLine(string text) {
            try {
                surfaceSession.PublishSurfaceOperation(SurfaceOperations.Stream(
                    StreamPayloadKind.AppendLine,
                    text ?? string.Empty,
                    channel: StreamChannel.Transcript));
            }
            catch (ObjectDisposedException) {
            }
        }

        private void PublishConsoleStreamLine(string text, ConsoleColor foreground, ConsoleColor background) {
            try {
                PublishStyledStream(
                    StreamPayloadKind.AppendLine,
                    CreateConsoleStyledLine(text, foreground, background),
                    CreateConsoleStyleDictionary(foreground, background));
            }
            catch (ObjectDisposedException) {
            }
        }

        private void PublishStyledStreamLine(StyledTextLine line, StyleDictionary? styles = null) {
            try {
                PublishStyledStream(StreamPayloadKind.AppendLine, line, styles ?? TranscriptFormatter.StyleDictionary);
            }
            catch (ObjectDisposedException) {
            }
        }

        private void PublishStyledStream(StreamPayloadKind kind, StyledTextLine line, StyleDictionary? styles) {
            surfaceSession.PublishSurfaceOperation(SurfaceOperations.Stream(
                kind,
                line,
                styles,
                channel: StreamChannel.Transcript));
        }

        private static StyledTextLine CreateConsoleStyledLine(string? text, ConsoleColor foreground, ConsoleColor background) {
            var value = text ?? string.Empty;
            return new StyledTextLine {
                Runs = string.IsNullOrEmpty(value)
                    ? []
                    : [
                        new StyledTextRun {
                            Text = value,
                            StyleId = CreateConsoleStyleId(foreground, background),
                        },
                    ],
            };
        }

        private static StyleDictionary CreateConsoleStyleDictionary(ConsoleColor foreground, ConsoleColor background) {
            return new StyleDictionary {
                Styles = [
                    new StyledTextStyle {
                        StyleId = CreateConsoleStyleId(foreground, background),
                        Foreground = ToStyledColor(foreground),
                        Background = ToStyledColor(background),
                    },
                ],
            };
        }

        private static string CreateConsoleStyleId(ConsoleColor foreground, ConsoleColor background) {
            return $"console.{foreground}.{background}";
        }

        private static StyledColorValue ToStyledColor(ConsoleColor color) {
            var (red, green, blue) = color switch {
                ConsoleColor.Black => (0, 0, 0),
                ConsoleColor.DarkBlue => (0, 0, 128),
                ConsoleColor.DarkGreen => (0, 128, 0),
                ConsoleColor.DarkCyan => (0, 128, 128),
                ConsoleColor.DarkRed => (128, 0, 0),
                ConsoleColor.DarkMagenta => (128, 0, 128),
                ConsoleColor.DarkYellow => (128, 128, 0),
                ConsoleColor.Gray => (192, 192, 192),
                ConsoleColor.DarkGray => (128, 128, 128),
                ConsoleColor.Blue => (0, 0, 255),
                ConsoleColor.Green => (0, 255, 0),
                ConsoleColor.Cyan => (0, 255, 255),
                ConsoleColor.Red => (255, 0, 0),
                ConsoleColor.Magenta => (255, 0, 255),
                ConsoleColor.Yellow => (255, 255, 0),
                ConsoleColor.White => (255, 255, 255),
                _ => (192, 192, 192),
            };
            return new StyledColorValue {
                Red = (byte)red,
                Green = (byte)green,
                Blue = (byte)blue,
            };
        }

        #endregion

        #region Console Read Bridge

        private string? ReadConsoleLine() {
            EnterConsoleReadGate();
            try {
                if (TryReadBufferedConsoleLine(out var bufferedLine)) {
                    return bufferedLine;
                }

                return WaitForConsoleInputLine();
            }
            finally {
                consoleReadGate.Release();
            }
        }

        private int ReadConsoleChar() {
            EnterConsoleReadGate();
            try {
                if (TryReadBufferedConsoleChar(out var bufferedChar)) {
                    return bufferedChar;
                }

                var line = WaitForConsoleInputLine() ?? string.Empty;
                EnqueueConsoleInputLine(line);
                return TryReadBufferedConsoleChar(out bufferedChar) ? bufferedChar : -1;
            }
            finally {
                consoleReadGate.Release();
            }
        }

        private ConsoleKeyInfo ReadConsoleKey(bool intercept) {
            EnterConsoleReadGate();
            try {
                return WaitForConsoleKey(intercept);
            }
            finally {
                consoleReadGate.Release();
            }
        }

        private void EnterConsoleReadGate() {
            try {
                consoleReadGate.Wait(processingCancellation.Token);
            }
            catch (OperationCanceledException) when (processingCancellation.IsCancellationRequested) {
                throw new ObjectDisposedException(nameof(ReplWindowAdapter));
            }
        }

        private string WaitForConsoleInputLine() {
            var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (sync) {
                ObjectDisposedException.ThrowIf(disposed, this);
                pendingConsoleInput = completion;
            }

            PublishConsoleInputFrame(captureRawKeys: false);
            try {
                return completion.Task.GetAwaiter().GetResult();
            }
            finally {
                lock (sync) {
                    if (ReferenceEquals(pendingConsoleInput, completion)) {
                        pendingConsoleInput = null;
                    }
                }
            }
        }

        private ConsoleKeyInfo WaitForConsoleKey(bool intercept) {
            var pendingRead = new PendingConsoleKeyRead(
                intercept,
                new TaskCompletionSource<ConsoleKeyInfo>(TaskCreationOptions.RunContinuationsAsynchronously));
            lock (sync) {
                ObjectDisposedException.ThrowIf(disposed, this);
                pendingConsoleKeyRead = pendingRead;
            }

            PublishConsoleInputFrame(captureRawKeys: true);
            try {
                return pendingRead.Completion.Task.GetAwaiter().GetResult();
            }
            finally {
                lock (sync) {
                    if (ReferenceEquals(pendingConsoleKeyRead, pendingRead)) {
                        pendingConsoleKeyRead = null;
                    }
                }
            }
        }

        private void PublishConsoleInputFrame(bool captureRawKeys) {
            var publication = session.CurrentPublication;
            var scope = EnsureConsoleInputScope();

            var document = CreateConsoleInputDocument(
                scope.Scope.Id,
                publication,
                ResolveNextConsoleClientBufferRevision(publication),
                string.Empty,
                0,
                captureRawKeys);
            try {
                scope.PublishDocument(document);
            }
            catch (ObjectDisposedException) {
            }
        }

        #endregion

        #region Blocking And Background Work

        private void EnterBlockingExecutionState() {
            lock (sync) {
                if (disposed) {
                    return;
                }

                blockingExecutionActive = true;
            }

            ShowBlockingScope();
        }

        private void ExitBlockingExecutionState() {
            lock (sync) {
                blockingExecutionActive = false;
            }

            RestoreAuthoringScope();
        }

        private void RestoreInteractionAfterConsoleInput() {
            bool blockingActive;
            lock (sync) {
                if (disposed
                    || pendingConsoleInput is not null
                    || pendingConsoleKeyRead is not null) {
                    return;
                }

                blockingActive = blockingExecutionActive;
            }

            if (blockingActive) {
                ShowBlockingScope();
                return;
            }

            PublishCurrentAuthoringDocument();
        }

        private void RestoreAuthoringScope() {
            try {
                if (processingCancellation.IsCancellationRequested) {
                    return;
                }

                ShowAuthoringScope(session.CurrentPublication);
            }
            catch (OperationCanceledException) {
            }
            catch (ObjectDisposedException) {
            }
        }

        private void TrackBackgroundTask(RunResult? runResult) {
            if (runResult is null) {
                return;
            }

            if (runResult.BackgroundExecution is { CompletionTask.IsCompleted: false } backgroundExecution) {
                TrackBackgroundTask(
                    backgroundExecution.CompletionTask,
                    new BackgroundTaskDescriptor(BackgroundTaskKind.Execution, backgroundExecution.Serial));
            }

            if (runResult.ReturnValue is Task detachedTask && !detachedTask.IsCompleted) {
                TrackBackgroundTask(
                    detachedTask,
                    new BackgroundTaskDescriptor(BackgroundTaskKind.ReturnValueTask, runResult.ExecutionSerial));
            }
        }

        private void TrackBackgroundTask(Task task, BackgroundTaskDescriptor descriptor) {
            bool shouldPublish;
            lock (sync) {
                if (disposed || !backgroundTasks.Add(task)) {
                    return;
                }

                PruneBackgroundTasksLocked();
                shouldPublish = ShouldShowInputIndicatorLocked() && backgroundTasks.Count == 1;
            }

            _ = task.ContinueWith(
                static (completedTask, state) => {
                    var (adapter, taskDescriptor) = ((ReplWindowAdapter Adapter, BackgroundTaskDescriptor Descriptor))state!;
                    adapter.OnBackgroundTaskCompleted(completedTask, taskDescriptor);
                },
                (this, descriptor),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            if (shouldPublish) {
                PublishCurrentAuthoringDocument();
            }
        }

        private void OnBackgroundTaskCompleted(Task completedTask, BackgroundTaskDescriptor descriptor) {
            bool shouldPublish;
            lock (sync) {
                if (disposed) {
                    return;
                }

                backgroundTasks.Remove(completedTask);
                PruneBackgroundTasksLocked();
                shouldPublish = backgroundTasks.Count == 0
                    && !blockingExecutionActive
                    && pendingConsoleInput is null
                    && pendingConsoleKeyRead is null;
            }

            if (descriptor.Kind == BackgroundTaskKind.Execution) {
                PublishBackgroundExecutionCompletion(completedTask);
            }

            if (shouldPublish) {
                PublishCurrentAuthoringDocument();
            }
        }

        private void PublishBackgroundExecutionCompletion(Task completedTask) {
            if (completedTask is not Task<RunResult> runTask) {
                return;
            }

            RunResult runResult;
            try {
                runResult = runTask.GetAwaiter().GetResult();
            }
            catch (Exception ex) {
                PublishErrorBox("BackgroundExecution", [ex.Message]);
                return;
            }

            switch (runResult.Operation) {
                case OperationKind.PersistentSubmit:
                    PublishPersistentSubmitResult(runResult);
                    break;

                case OperationKind.TransientRun:
                    PublishTransientRunResult(runResult);
                    break;
            }

            TrackBackgroundTask(runResult);
        }

        private void PruneBackgroundTasksLocked() {
            if (backgroundTasks.Count == 0) {
                return;
            }

            backgroundTasks.RemoveWhere(static task => task.IsCompleted);
        }

        private bool ShouldShowInputIndicatorLocked() {
            PruneBackgroundTasksLocked();
            return pendingConsoleInput is null
                && pendingConsoleKeyRead is null
                && !blockingExecutionActive
                && backgroundTasks.Count > 0;
        }

        #endregion

        #region Authoring Publication

        private PromptInputState CreateLatestRequestedPromptInputStateLocked() {
            return new PromptInputState {
                InputText = latestClientBase.Text,
                CursorIndex = latestClientBase.CaretIndex,
                CompletionIndex = latestRequestedCompletionIndex,
                CompletionCount = 0,
                PreferredCompletionText = latestRequestedPreferredCompletionText,
                PreferredInterpretationId = latestRequestedPreferredInterpretationId,
            };
        }

        private void PublishCurrentAuthoringDocument() {
            if (!TryGetAuthoringScope(out var scope)) {
                return;
            }

            SessionPublication publication;
            lock (sync) {
                if (disposed) {
                    return;
                }

                publication = session.CurrentPublication;
            }
            PublishCurrentAuthoringDocument(scope, publication);
        }

        private void PublishCurrentAuthoringDocument(AuthoringSurfaceInteractionScope scope, SessionPublication publication) {
            RemoteOverlayProposal? pendingProposal;
            PromptInputState inputState;
            string requestedCompletionItemId;
            IReadOnlyList<ClientBufferedTextMarker> markers;
            DraftSnapshot? draftSnapshot;
            IReadOnlyList<PromptHighlightSpan> sourceHighlights;
            long expectedClientBufferRevision;
            long remoteRevision;
            bool showInputIndicator;
            lock (sync) {
                if (disposed) {
                    return;
                }

                showInputIndicator = ShouldShowInputIndicatorLocked();
                pendingProposal = remoteOverlayLedger.Latest;
                if (pendingProposal is not null) {
                    inputState = CreatePromptInputState(pendingProposal.ProjectedState);
                    requestedCompletionItemId = latestRequestedCompletionItemId;
                    markers = pendingProposal.ProjectedState.Markers;
                    draftSnapshot = pendingProposal.ProjectedDraft;
                    sourceHighlights = pendingProposal.ProjectedSourceHighlights;
                    expectedClientBufferRevision = pendingProposal.BaseSnapshot.ClientBufferRevision;
                    remoteRevision = pendingProposal.RemoteRevision;
                }
                else if (MatchesLatestDraftLocked(publication)) {
                    inputState = CreatePublicationInputStateLocked(publication);
                    requestedCompletionItemId = latestRequestedCompletionItemId;
                    markers = publication.Revision.Markers;
                    draftSnapshot = latestClientBaseDraft;
                    sourceHighlights = publication.Workspace.DraftSourceHighlights;
                    expectedClientBufferRevision = Math.Max(0, publication.Revision.ClientBufferRevision);
                    remoteRevision = latestClientBase.AcceptedRemoteRevision;
                }
                else {
                    inputState = CreateLatestRequestedPromptInputStateLocked();
                    requestedCompletionItemId = string.Empty;
                    markers = latestClientBase.Markers;
                    draftSnapshot = latestClientBaseDraft;
                    sourceHighlights = latestClientBaseSourceHighlights;
                    expectedClientBufferRevision = Math.Max(0, latestClientBase.ClientBufferRevision);
                    remoteRevision = latestClientBase.AcceptedRemoteRevision;
                }
            }

            try {
                scope.PublishDocument(CreatePublicationDocument(
                        scope.Scope.Id,
                        publication,
                        inputState,
                        expectedClientBufferRevision,
                        remoteRevision,
                        requestedCompletionItemId,
                        markers,
                        sourceHighlights,
                        draftSnapshot: draftSnapshot,
                        showInputIndicator: showInputIndicator));
            }
            catch (ObjectDisposedException) {
            }
        }

        private long ResolveNextConsoleClientBufferRevision(SessionPublication publication) {
            lock (sync) {
                var nextRevision = Math.Max(latestClientBase.ClientBufferRevision, publication.Revision.ClientBufferRevision) + 1;
                remoteOverlayLedger.ClearCurrentProjection();
                latestClientBase = new EditorBufferSnapshot(
                    nextRevision,
                    latestClientBase.AcceptedRemoteRevision,
                    EditorPaneKind.MultiLine,
                    string.Empty,
                    0,
                    [],
                    [],
                    []);
                latestClientBaseDraft = DraftSnapshot.Empty;
                latestRequestedCompletionIndex = 0;
                latestRequestedCompletionItemId = string.Empty;
                latestRequestedPreferredCompletionText = string.Empty;
                latestRequestedPreferredInterpretationId = string.Empty;
                latestEditorSyncState = null;
                latestEditorSyncSerial = checked(latestEditorSyncSerial + 1);
                latestAuthoringState = null;
                latestAuthoringSerial = checked(latestAuthoringSerial + 1);
                consoleInteractionActive = true;
                editorSyncVersion = checked(editorSyncVersion + 1);
                return nextRevision;
            }
        }

        #endregion

        #region Console Input Buffer

        private ProjectionDocument CreateConsoleInputDocument(
            InteractionScopeId scopeId,
            SessionPublication publication,
            long expectedClientBufferRevision,
            string bufferText,
            int caretIndex,
            bool captureRawKeys) {
            var document = CreatePublicationDocument(
                scopeId,
                publication,
                CreateSourcePromptInputState(publication),
                Math.Max(0, publication.Revision.ClientBufferRevision),
                0,
                requestedCompletionItemId: string.Empty,
                markers: publication.Revision.Markers,
                sourceHighlights: publication.Workspace.DraftSourceHighlights,
                captureRawKeys: captureRawKeys);
            var normalizedBuffer = bufferText ?? string.Empty;
            return ReplaceInputState(
                document,
                normalizedBuffer,
                Math.Clamp(caretIndex, 0, normalizedBuffer.Length),
                Math.Max(0, expectedClientBufferRevision));
        }

        private bool TryReadBufferedConsoleLine(out string line) {
            lock (sync) {
                if (consoleInputBuffer.Count == 0) {
                    line = string.Empty;
                    return false;
                }

                StringBuilder builder = new();
                while (consoleInputBuffer.Count > 0) {
                    var ch = consoleInputBuffer.Dequeue();
                    if (ch == '\r') {
                        if (consoleInputBuffer.Count > 0 && consoleInputBuffer.Peek() == '\n') {
                            consoleInputBuffer.Dequeue();
                        }

                        line = builder.ToString();
                        return true;
                    }

                    if (ch == '\n') {
                        line = builder.ToString();
                        return true;
                    }

                    builder.Append(ch);
                }

                line = builder.ToString();
                return true;
            }
        }

        private bool TryReadBufferedConsoleChar(out int value) {
            lock (sync) {
                if (consoleInputBuffer.Count == 0) {
                    value = -1;
                    return false;
                }

                value = consoleInputBuffer.Dequeue();
                return true;
            }
        }

        private void EnqueueConsoleInputLine(string line) {
            var normalized = line ?? string.Empty;
            lock (sync) {
                foreach (var ch in normalized) {
                    consoleInputBuffer.Enqueue(ch);
                }

                foreach (var ch in Environment.NewLine) {
                    consoleInputBuffer.Enqueue(ch);
                }
            }
        }

        private void PublishConsoleInputEcho(string line) {
            try {
                surfaceSession.PublishSurfaceOperation(SurfaceOperations.Stream(
                    StreamPayloadKind.AppendLine,
                    line ?? string.Empty,
                    channel: StreamChannel.Transcript));
            }
            catch (ObjectDisposedException) {
            }
        }

        private void PublishConsoleKeyEcho(ConsoleKeyInfo keyInfo) {
            if (keyInfo.KeyChar == '\0') {
                return;
            }

            if (keyInfo.KeyChar is '\r' or '\n') {
                session.Console.WriteLine();
                return;
            }

            if (char.IsControl(keyInfo.KeyChar)) {
                return;
            }

            session.Console.Write(keyInfo.KeyChar);
        }

        private void EndConsoleInteraction() {
            TaskCompletionSource<string>? completion = null;
            TaskCompletionSource<ConsoleKeyInfo>? keyCompletion = null;
            var publication = session.CurrentPublication;
            lock (sync) {
                consoleInteractionActive = false;
                if (pendingConsoleInput is not null) {
                    completion = pendingConsoleInput;
                    pendingConsoleInput = null;
                }

                if (pendingConsoleKeyRead is { } pendingKeyRead) {
                    keyCompletion = pendingKeyRead.Completion;
                    pendingConsoleKeyRead = null;
                }

                var text = publication.SourceText ?? string.Empty;
                var markers = publication.Revision.Markers ?? [];
                var caret = Math.Clamp(publication.Revision.CaretIndex, 0, text.Length);
                var restoredState = new ClientBufferedEditorState {
                    Kind = EditorPaneKind.MultiLine,
                    BufferText = text,
                    CaretIndex = caret,
                    ClientBufferRevision = Math.Max(0, publication.Revision.ClientBufferRevision),
                    AcceptedRemoteRevision = latestClientBase.AcceptedRemoteRevision,
                    Markers = [.. markers],
                };
                var restoredDraft = latestClientBase.HasSameVisibleBuffer(restoredState)
                    ? latestClientBaseDraft
                    : ReplPairEngine.DecodeBaseDraft(restoredState);
                remoteOverlayLedger.ClearCurrentProjection();
                latestClientBase = new EditorBufferSnapshot(
                    restoredState.ClientBufferRevision,
                    latestClientBase.AcceptedRemoteRevision,
                    restoredState.Kind,
                    text,
                    caret,
                    [.. markers],
                    [],
                    []);
                latestClientBaseDraft = restoredDraft;
                latestRequestedCompletionIndex = 0;
                latestRequestedCompletionItemId = string.Empty;
                latestRequestedPreferredCompletionText = publication.Workspace.Completion.PreferredCompletionText;
                latestRequestedPreferredInterpretationId = string.Empty;
                latestEditorSyncState = null;
                latestEditorSyncSerial = checked(latestEditorSyncSerial + 1);
                latestAuthoringState = null;
                latestAuthoringSerial = checked(latestAuthoringSerial + 1);
                editorSyncVersion = checked(editorSyncVersion + 1);
            }

            completion?.TrySetException(new OperationCanceledException("Console input ended before a value was submitted."));
            keyCompletion?.TrySetException(new OperationCanceledException("Console key input ended before a value was submitted."));
        }

        private void CancelPendingConsoleReads(Exception exception) {
            TaskCompletionSource<string>? completion;
            TaskCompletionSource<ConsoleKeyInfo>? keyCompletion;
            lock (sync) {
                completion = pendingConsoleInput;
                pendingConsoleInput = null;
                keyCompletion = pendingConsoleKeyRead?.Completion;
                pendingConsoleKeyRead = null;
                consoleInteractionActive = false;
            }

            completion?.TrySetException(exception);
            keyCompletion?.TrySetException(exception);
        }

        #endregion

        #region Result And Error Output

        private void PublishSubmittedInput(string sourceText, IReadOnlyList<PromptHighlightSpan>? highlights = null) {
            foreach (var line in TranscriptFormatter.FormatSubmittedInput(sourceText, highlights)) {
                PublishStyledStreamLine(line);
            }
        }

        private void PublishPersistentSubmitResult(RunResult runResult) {
            switch (runResult.Outcome) {
                case OutcomeKind.Executed:
                    if (runResult.HasReturnValue) {
                        foreach (var line in TranscriptFormatter.FormatReturnValue(runResult.ReturnValue)) {
                            PublishStyledStreamLine(line);
                        }
                    }
                    break;

                case OutcomeKind.CompilationFailed:
                    PublishErrorBox(
                        "CompilationErrorException",
                        CollectErrorLines(runResult));
                    break;

                case OutcomeKind.RuntimeFailed:
                    PublishErrorBox(
                        runResult.Exception?.GetType().Name ?? "Exception",
                        CollectErrorLines(runResult));
                    break;

                case OutcomeKind.Cancelled:
                    PublishStreamLine("Execution cancelled.");
                    break;

                case OutcomeKind.Invalidated:
                    PublishErrorBox(
                        "SessionInvalidated",
                        CollectErrorLines(runResult));
                    break;
            }
        }

        private void PublishTransientRunResult(RunResult runResult) {
            if (runResult.Outcome == OutcomeKind.Invalidated) {
                PublishStreamLine(runResult.Exception?.Message ?? "Session invalidated. Reopen the Atelier REPL session.");
                return;
            }

            if (runResult.Outcome == OutcomeKind.Cancelled) {
                PublishStreamLine("Transient execution cancelled.");
                return;
            }

            if (runResult.ExecutionPhase == ExecutionPhase.Background && runResult.Outcome == OutcomeKind.Pending) {
                PublishStreamLine("Transient execution is running in the background.");
                return;
            }

            PublishStreamLine($"Transient outcome: {runResult.Outcome}.");
            if (runResult.HasReturnValue) {
                PublishStreamLine($"Transient result: {TranscriptFormatter.FormatReturnValueSummary(runResult.ReturnValue)}");
            }

            if (runResult.Exception is not null) {
                PublishStreamLine($"Transient exception: {runResult.Exception.GetType().Name}: {runResult.Exception.Message}");
            }

            foreach (var diagnostic in runResult.Diagnostics.Take(3)) {
                PublishStreamLine(diagnostic.DisplayText);
            }

            if (runResult.Diagnostics.Length > 3) {
                PublishStreamLine($"... {runResult.Diagnostics.Length - 3} more diagnostic(s).");
            }
        }

        private void PublishErrorBox(string title, IReadOnlyList<string> lines) {
            foreach (var line in TranscriptFormatter.FormatErrorBox(title, lines)) {
                PublishStyledStreamLine(line);
            }
        }

        private static IReadOnlyList<string> CollectErrorLines(RunResult runResult) {
            List<string> lines = [];
            if (runResult.Diagnostics.Length > 0) {
                foreach (var diagnostic in runResult.Diagnostics.Take(8)) {
                    lines.Add(diagnostic.DisplayText);
                }

                if (runResult.Diagnostics.Length > 8) {
                    lines.Add($"... {runResult.Diagnostics.Length - 8} more diagnostic(s).");
                }
            }
            else if (runResult.Exception is not null) {
                lines.Add(runResult.Exception.Message);
            }
            else {
                lines.Add("Execution failed.");
            }

            return lines;
        }

        private void PublishStartupFailure(Exception exception) {
            var baseException = exception.GetBaseException();
            var detail = NormalizeSingleLine(string.IsNullOrWhiteSpace(baseException.Message)
                ? baseException.GetType().Name
                : $"{baseException.GetType().Name}: {baseException.Message}");
            if (TryGetAuthoringScope(out _)) {
                PublishErrorBox("InitializationError", [$"Atelier REPL failed to initialize: {detail}"]);
                return;
            }

            try {
                ShowBlockingScope($"Atelier REPL failed to initialize: {detail}");
            }
            catch (ObjectDisposedException) {
            }
        }

        private void PublishWarmupFailure(Exception exception) {
            var baseException = exception.GetBaseException();
            var detail = NormalizeSingleLine(string.IsNullOrWhiteSpace(baseException.Message)
                ? baseException.GetType().Name
                : $"{baseException.GetType().Name}: {baseException.Message}");
            PublishErrorBox("SemanticWarmup", [$"Atelier semantic warmup failed: {detail}"]);
        }

        private static string NormalizeSingleLine(string text) {
            return (text ?? string.Empty)
                .Replace("\r", " ", StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal)
                .Trim();
        }

        private static ProjectionTextBlock CreateSingleLineBlock(string text, string styleKey = "", string lineStyleKey = "") {
            return new ProjectionTextBlock {
                Lines = [
                    new ProjectionTextLine {
                        Style = ProjectionStyleDictionaryOps.Reference(lineStyleKey),
                        Spans = [
                            new ProjectionTextSpan {
                                Text = (text ?? string.Empty)
                                    .Replace("\r", " ", StringComparison.Ordinal)
                                    .Replace("\n", " ", StringComparison.Ordinal),
                                Style = ProjectionStyleDictionaryOps.Reference(styleKey),
                            },
                        ],
                    },
                ],
            };
        }

        #endregion

        #region Projection Documents

        private void PublishIfCurrent(SessionPublication publication) {
            lock (sync) {
                if (disposed || !ShouldPublishPublicationLocked(publication)) {
                    return;
                }

                RefreshPublicationHighlightsLocked(publication);
            }

            PublishCurrentAuthoringDocument();
        }

        private void RefreshPublicationHighlightsLocked(SessionPublication publication) {
            if (MatchesLatestDraftLocked(publication)) {
                latestClientBaseSourceHighlights = [.. publication.Workspace.DraftSourceHighlights];
            }

            if (remoteOverlayLedger.Latest is { } pendingOverlay
                && MatchesEditorState(publication, pendingOverlay.ProjectedState)) {
                pendingOverlay.UpdateSourceHighlights(publication.Workspace.DraftSourceHighlights);
            }
        }

        private bool ShouldPublishPublicationLocked(SessionPublication publication) {
            if (publication.Revision.ClientBufferRevision > latestClientBase.ClientBufferRevision) {
                return true;
            }

            if (publication.Revision.ClientBufferRevision < latestClientBase.ClientBufferRevision) {
                return false;
            }

            return MatchesLatestDraftLocked(publication)
                || remoteOverlayLedger.Latest is { } pendingOverlay
                && MatchesEditorState(publication, pendingOverlay.ProjectedState);
        }

        private PromptInputState CreatePublicationInputStateLocked(SessionPublication publication) {
            bool matchesLatestDraft = MatchesLatestDraftLocked(publication);
            var sourceText = publication.SourceText ?? string.Empty;
            int completionCount = publication.Workspace.Completion.Items.Length;
            return new PromptInputState {
                InputText = sourceText,
                CursorIndex = publication.Revision.CaretIndex,
                CompletionIndex = matchesLatestDraft
                    ? Math.Clamp(latestRequestedCompletionIndex, 0, completionCount)
                    : 0,
                CompletionCount = completionCount,
                PreferredCompletionText = matchesLatestDraft && !string.IsNullOrWhiteSpace(latestRequestedPreferredCompletionText)
                    ? latestRequestedPreferredCompletionText
                    : publication.Workspace.Completion.PreferredCompletionText,
                PreferredInterpretationId = matchesLatestDraft ? latestRequestedPreferredInterpretationId : string.Empty,
            };
        }

        private bool MatchesLatestDraftLocked(SessionPublication publication) {
            return publication.Revision.ClientBufferRevision == latestClientBase.ClientBufferRevision
                && string.Equals(publication.SourceText, latestClientBase.Text, StringComparison.Ordinal)
                && publication.Revision.CaretIndex == latestClientBase.CaretIndex
                && EditorTextMarkerOps.ContentEquals(publication.Revision.Markers, latestClientBase.Markers);
        }

        private static bool MatchesEditorState(SessionPublication publication, ClientBufferedEditorState state) {
            var text = state.BufferText ?? string.Empty;
            return publication.Revision.ClientBufferRevision == Math.Max(0, state.ClientBufferRevision)
                && string.Equals(publication.SourceText, text, StringComparison.Ordinal)
                && publication.Revision.CaretIndex == Math.Clamp(state.CaretIndex, 0, text.Length)
                && EditorTextMarkerOps.ContentEquals(publication.Revision.Markers, state.Markers);
        }

        private void PublishDraftDocument(
            SessionPublication publication,
            ClientBufferedEditorState bufferedState,
            DraftSnapshot draftSnapshot,
            IReadOnlyList<PromptHighlightSpan> sourceHighlights,
            long remoteRevision) {
            if (!TryGetAuthoringScope(out var scope)) {
                return;
            }

            bool showInputIndicator;
            lock (sync) {
                if (disposed) {
                    return;
                }

                showInputIndicator = ShouldShowInputIndicatorLocked();
            }

            try {
                scope.PublishDocument(CreatePublicationDocument(
                        scope.Scope.Id,
                        publication,
                        CreatePromptInputState(bufferedState),
                        Math.Max(0, bufferedState.ClientBufferRevision),
                        Math.Max(0, remoteRevision),
                        requestedCompletionItemId: latestRequestedCompletionItemId,
                        markers: bufferedState.Markers,
                        sourceHighlights: sourceHighlights,
                        draftSnapshot: draftSnapshot,
                        showInputIndicator: showInputIndicator));
            }
            catch (ObjectDisposedException) {
            }
        }

        private static PromptInputState CreatePromptInputState(ClientBufferedEditorState bufferedState) {
            var text = bufferedState.BufferText ?? string.Empty;
            var completionSelection = bufferedState.FindSelection(EditorProjectionSemanticKeys.AssistPrimaryList);
            var previewSelection = bufferedState.FindSelection(EditorProjectionSemanticKeys.InputGhost);
            var interpretationSelection = bufferedState.FindSelection(EditorProjectionSemanticKeys.AssistSecondaryList);
            return new PromptInputState {
                InputText = text,
                CursorIndex = Math.Clamp(bufferedState.CaretIndex, 0, text.Length),
                CompletionIndex = Math.Max(0, completionSelection?.ActiveOrdinal ?? 0),
                PreferredCompletionText = previewSelection?.ActiveItemId ?? string.Empty,
                PreferredInterpretationId = interpretationSelection?.ActiveItemId ?? string.Empty,
            };
        }

        private IReadOnlyList<PromptHighlightSpan> ResolveSubmittedInputHighlights(string submittedSourceText) {
            var publication = session.CurrentPublication;
            return string.Equals(publication.SyntheticDocument.DraftText, submittedSourceText, StringComparison.Ordinal)
                ? [.. publication.Workspace.DraftSourceHighlights]
                : [];
        }

        private static PromptInputState CreateSourcePromptInputState(SessionPublication publication) {
            var sourceText = publication.SourceText ?? string.Empty;
            return new PromptInputState {
                InputText = sourceText,
                CursorIndex = publication.Revision.CaretIndex,
                CompletionCount = publication.Workspace.Completion.Items.Length,
                PreferredCompletionText = publication.Workspace.Completion.PreferredCompletionText,
            };
        }

        private ProjectionDocument CreatePublicationDocument(
            InteractionScopeId scopeId,
            SessionPublication publication,
            PromptInputState promptInputState,
            long expectedClientBufferRevision,
            long remoteRevision,
            string requestedCompletionItemId = "",
            IReadOnlyList<ClientBufferedTextMarker>? markers = null,
            IReadOnlyList<PromptHighlightSpan>? sourceHighlights = null,
            bool captureRawKeys = false,
            bool showInputIndicator = false,
            DraftSnapshot? draftSnapshot = null) {
            var resolvedInputState = promptInputState ?? CreateSourcePromptInputState(publication);
            draftSnapshot ??= DraftMarkers.DecodeSnapshot(
                resolvedInputState.InputText,
                markers,
                resolvedInputState.CursorIndex);
            var content = promptProjectionBuilder.Build(
                publication,
                new PromptDocumentRequest(
                    resolvedInputState,
                    Math.Max(0, expectedClientBufferRevision),
                    Math.Max(0, remoteRevision),
                    requestedCompletionItemId,
                    markers is null ? null : [.. markers],
                    session.ParseOptions,
                    captureRawKeys,
                    showInputIndicator ? PromptSpinnerFrameBlocks[0] : null,
                    showInputIndicator ? PromptIndicatorAnimation : null,
                    draftSnapshot,
                    session.UseSmartSubmitDetection,
                    InputHighlights: HighlightProjection.ProjectSourceHighlights(sourceHighlights, draftSnapshot),
                    SubmitKeyMode: GetSubmitKeyMode()));
            return PromptProjectionDocumentFactory.CreateDocument(
                scopeId,
                AtelierIds.AuthoringInteractionKind,
                content);
        }

        private static ProjectionDocument CreateBlockingDocument(InteractionScopeId scopeId, string? text = null) {
            var (summary, detailLines) = CreateBlockingStatusContent(text);
            const string rootNodeId = "atelier.blocking.root";
            const string summaryNodeId = "atelier.blocking.summary";
            const string detailNodeId = "atelier.blocking.detail";
            return new ProjectionDocument {
                Scope = new ProjectionScope {
                    Kind = ProjectionScopeKind.Interaction,
                    ScopeId = scopeId.Value,
                    DocumentKind = AtelierIds.BlockingInteractionKind,
                },
                Definition = new ProjectionDocumentDefinition {
                    RootNodeIds = [rootNodeId],
                    Traits = new ProjectionDocumentTraits {
                        SupportsFocus = false,
                        SupportsSelection = false,
                    },
                    Nodes = [
                        new ProjectionNodeDefinition {
                            NodeId = rootNodeId,
                            Kind = ProjectionNodeKind.Container,
                            ChildNodeIds = [summaryNodeId, detailNodeId],
                            Traits = new ProjectionNodeTraits {
                                HideWhenEmpty = true,
                            },
                        },
                        new ProjectionNodeDefinition {
                            NodeId = summaryNodeId,
                            Kind = ProjectionNodeKind.Text,
                            Role = ProjectionSemanticRole.Feedback,
                            SemanticKey = EditorProjectionSemanticKeys.StatusSummary,
                            Zone = ProjectionZone.Support,
                            Traits = new ProjectionNodeTraits {
                                HideWhenEmpty = true,
                            },
                        },
                        new ProjectionNodeDefinition {
                            NodeId = detailNodeId,
                            Kind = ProjectionNodeKind.Detail,
                            Role = ProjectionSemanticRole.Detail,
                            SemanticKey = EditorProjectionSemanticKeys.StatusDetail,
                            Zone = ProjectionZone.Detail,
                            Traits = new ProjectionNodeTraits {
                                HideWhenEmpty = true,
                            },
                        },
                    ],
                },
                State = new ProjectionDocumentState {
                    Styles = PromptProjectionDocumentFactory.CreateProjectionStyleDictionary(PromptStyles.Default),
                    Nodes = [
                        new ContainerProjectionNodeState {
                            NodeId = rootNodeId,
                            State = new ContainerNodeState(),
                        },
                        new TextProjectionNodeState {
                            NodeId = summaryNodeId,
                            State = new TextNodeState {
                                Content = CreateSingleLineBlock(
                                    summary,
                                    SurfaceStyleCatalog.StatusSummary,
                                    SurfaceStyleCatalog.StatusBand),
                            },
                        },
                        new DetailProjectionNodeState {
                            NodeId = detailNodeId,
                            State = new DetailNodeState {
                                Lines = [.. detailLines.Select(line => CreateSingleLineBlock(line, SurfaceStyleCatalog.StatusDetail))],
                            },
                        },
                    ],
                },
            };
        }

        private static (string Summary, IReadOnlyList<string> DetailLines) CreateBlockingStatusContent(string? text) {
            if (string.IsNullOrWhiteSpace(text)) {
                return (string.Empty, []);
            }

            var lines = text
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Split('\n')
                .Select(NormalizeSingleLine)
                .Where(static line => !string.IsNullOrWhiteSpace(line))
                .ToArray();
            return lines.Length == 0
                ? (string.Empty, [])
                : (lines[0], lines.Length == 1 ? [] : lines[1..]);
        }

        private static ProjectionDocument ReplaceInputState(
            ProjectionDocument document,
            string bufferText,
            int caretIndex,
            long expectedClientBufferRevision) {
            var inputNodeId = (document.Definition.Nodes ?? [])
                .FirstOrDefault(node => node.Kind == ProjectionNodeKind.EditableText && node.Role == ProjectionSemanticRole.Input)
                ?.NodeId
                ?? throw new InvalidOperationException(GetString("Atelier projection document is missing an editable input node."));
            var nodes = (document.State.Nodes ?? [])
                .Select(node => node is EditableTextProjectionNodeState editable && string.Equals(node.NodeId, inputNodeId, StringComparison.Ordinal)
                    ? new EditableTextProjectionNodeState {
                        NodeId = editable.NodeId,
                        State = new EditableTextNodeState {
                            BufferText = bufferText,
                            CaretIndex = caretIndex,
                            ExpectedClientBufferRevision = expectedClientBufferRevision,
                            RemoteRevision = editable.State.RemoteRevision,
                            Markers = [.. (editable.State.Markers ?? [])],
                            Decorations = editable.State.Decorations,
                            Submit = editable.State.Submit,
                        },
                    }
                    : node)
                .ToArray();
            return new ProjectionDocument {
                Scope = document.Scope,
                Definition = document.Definition,
                State = new ProjectionDocumentState {
                    Focus = document.State.Focus,
                    Selection = document.State.Selection,
                    Styles = document.State.Styles,
                    Nodes = nodes,
                },
            };
        }

        #endregion

        #region Surface Events

        private void OnKeyReceived(ConsoleKeyInfo keyInfo) {
            PendingConsoleKeyRead? pendingRead;
            var formatEditor = false;
            lock (sync) {
                if (disposed) {
                    return;
                }

                if (pendingConsoleKeyRead is { } activeRead) {
                    pendingRead = activeRead;
                    pendingConsoleKeyRead = null;
                }
                else {
                    pendingRead = null;
                    formatEditor = !consoleInteractionActive && IsFormatEditorChord(keyInfo);
                    if (formatEditor) {
                        latestEditorSyncState = null;
                        latestEditorSyncSerial = checked(latestEditorSyncSerial + 1);
                    }
                }
            }

            if (formatEditor) {
                commandQueue.Writer.TryWrite(new AdapterCommand(AdapterCommandKind.FormatEditor));
                return;
            }

            if (pendingRead is null) {
                return;
            }

            if (!pendingRead.Intercept) {
                PublishConsoleKeyEcho(keyInfo);
            }

            pendingRead.Completion.TrySetResult(keyInfo);
            RestoreInteractionAfterConsoleInput();
        }

        private static bool IsFormatEditorChord(ConsoleKeyInfo keyInfo) {
            return keyInfo.Key == ConsoleKey.Tab
                && keyInfo.Modifiers == ConsoleModifiers.Shift;
        }

        private void OnSessionPublicationChanged(SessionPublication publication) {
            lock (sync) {
                if (disposed) {
                    return;
                }
            }

            commandQueue.Writer.TryWrite(new AdapterCommand(AdapterCommandKind.Publication, Publication: publication));
        }

        private void OnScopeTerminated(SurfaceInteractionTermination termination) {
            Dispose();
        }

        private void OnReleaseRequested() {
            Dispose();
        }

        private bool TryAcceptConsoleInput(ClientBufferedEditorState bufferedState) {
            TaskCompletionSource<string>? completion;
            lock (sync) {
                if (disposed || pendingConsoleInput is null) {
                    return false;
                }

                completion = pendingConsoleInput;
                pendingConsoleInput = null;
            }

            var submittedText = bufferedState.BufferText ?? string.Empty;
            PublishConsoleInputEcho(submittedText);
            completion.TrySetResult(submittedText);
            RestoreInteractionAfterConsoleInput();
            return true;
        }

        #endregion

        #region Scope Management

        private static SurfaceInteractionScopeOptions CreateAuthoringScopeOptions() {
            return new SurfaceInteractionScopeOptions(
                AtelierIds.AuthoringInteractionKind,
                IsTransient: false);
        }

        private static SurfaceInteractionScopeOptions CreateBlockingScopeOptions() {
            return new SurfaceInteractionScopeOptions(
                AtelierIds.BlockingInteractionKind,
                IsTransient: true);
        }

        private void ShowAuthoringScope(SessionPublication publication) {
            var scope = SwitchAuthoringScope();
            PublishCurrentAuthoringDocument(scope, publication);
            scope.Start();
        }

        private void ShowBlockingScope(string? text = null) {
            var scope = SwitchBlockingScope();
            scope.PublishDocument(CreateBlockingDocument(scope.Scope.Id, text));
            scope.Start();
        }

        private AuthoringSurfaceInteractionScope EnsureConsoleInputScope() {
            if (TryGetAuthoringScope(out var scope)) {
                return scope;
            }

            scope = SwitchAuthoringScope();
            scope.Start();
            return scope;
        }

        private AuthoringSurfaceInteractionScope SwitchAuthoringScope() {
            return SwitchScope(
                CreateAuthoringScopeOptions(),
                InteractionScopeMode.Authoring,
                static (surfaceSession, options) => new AuthoringSurfaceInteractionScope(surfaceSession, options));
        }

        private UnifierTSL.Surface.Interactions.SurfaceInteractionScope SwitchBlockingScope() {
            return SwitchScope(
                CreateBlockingScopeOptions(),
                InteractionScopeMode.Blocking,
                static (surfaceSession, options) => new UnifierTSL.Surface.Interactions.SurfaceInteractionScope(surfaceSession, options));
        }

        private TScope SwitchScope<TScope>(
            SurfaceInteractionScopeOptions options,
            InteractionScopeMode mode,
            Func<ISurfaceSession, SurfaceInteractionScopeOptions, TScope> createScope)
            where TScope : UnifierTSL.Surface.Interactions.SurfaceInteractionScope {
            ReleaseActiveScope();
            return EnsureScope(options, mode, createScope);
        }

        private TScope EnsureScope<TScope>(
            SurfaceInteractionScopeOptions options,
            InteractionScopeMode mode,
            Func<ISurfaceSession, SurfaceInteractionScopeOptions, TScope> createScope)
            where TScope : UnifierTSL.Surface.Interactions.SurfaceInteractionScope {
            lock (sync) {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (interactionScope is not null) {
                    if (interactionScopeMode != mode) {
                        throw new InvalidOperationException(GetString($"Atelier REPL expected active scope mode {mode} but found {interactionScopeMode}."));
                    }

                    if (interactionScope is not TScope typedScope) {
                        throw new InvalidOperationException(GetString($"Atelier REPL expected active scope type {typeof(TScope).Name} but found {interactionScope.GetType().Name}."));
                    }

                    return typedScope;
                }
            }

            var scope = createScope(surfaceSession, options);
            AttachScopeHandlers(scope, mode);
            lock (sync) {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (interactionScope is not null) {
                    if (interactionScopeMode != mode) {
                        DetachScopeHandlers(scope, mode);
                        DisposeScope(scope);
                        throw new InvalidOperationException(GetString($"Atelier REPL expected active scope mode {mode} but found {interactionScopeMode}."));
                    }

                    DetachScopeHandlers(scope, mode);
                    DisposeScope(scope);
                    if (interactionScope is not TScope typedScope) {
                        throw new InvalidOperationException(GetString($"Atelier REPL expected active scope type {typeof(TScope).Name} but found {interactionScope.GetType().Name}."));
                    }

                    return typedScope;
                }

                interactionScope = scope;
                interactionScopeMode = mode;
                return scope;
            }
        }

        private void ReleaseActiveScope() {
            UnifierTSL.Surface.Interactions.SurfaceInteractionScope? scope;
            InteractionScopeMode mode;
            lock (sync) {
                if (interactionScope is null) {
                    return;
                }

                scope = interactionScope;
                mode = interactionScopeMode;
                interactionScope = null;
                interactionScopeMode = InteractionScopeMode.None;
            }

            DetachScopeHandlers(scope, mode);
            try {
                scope.Complete();
            }
            catch {
            }

            DisposeScope(scope);
        }

        private bool TryGetScope(InteractionScopeMode mode, out UnifierTSL.Surface.Interactions.SurfaceInteractionScope scope) {
            lock (sync) {
                if (disposed || interactionScope is null || interactionScopeMode != mode) {
                    scope = null!;
                    return false;
                }

                scope = interactionScope;
                return true;
            }
        }

        private bool TryGetAuthoringScope(out AuthoringSurfaceInteractionScope scope) {
            if (TryGetScope(InteractionScopeMode.Authoring, out var activeScope)
                && activeScope is AuthoringSurfaceInteractionScope authoringScope) {
                scope = authoringScope;
                return true;
            }

            scope = null!;
            return false;
        }

        private void AttachScopeHandlers(UnifierTSL.Surface.Interactions.SurfaceInteractionScope scope, InteractionScopeMode mode) {
            scope.Terminated += OnScopeTerminated;
            if (mode != InteractionScopeMode.Authoring) {
                return;
            }

            if (scope is not AuthoringSurfaceInteractionScope authoringScope) {
                throw new InvalidOperationException(GetString("Authoring scope must expose editor interaction events."));
            }

            authoringScope.EditorStateSyncReceived += OnEditorStateSyncReceived;
            authoringScope.KeyReceived += OnKeyReceived;
            authoringScope.SubmitReceived += OnSubmitReceived;
        }

        private void DetachScopeHandlers(UnifierTSL.Surface.Interactions.SurfaceInteractionScope? scope, InteractionScopeMode mode) {
            if (scope is null) {
                return;
            }

            scope.Terminated -= OnScopeTerminated;
            if (mode != InteractionScopeMode.Authoring) {
                return;
            }

            if (scope is not AuthoringSurfaceInteractionScope authoringScope) {
                return;
            }

            authoringScope.EditorStateSyncReceived -= OnEditorStateSyncReceived;
            authoringScope.KeyReceived -= OnKeyReceived;
            authoringScope.SubmitReceived -= OnSubmitReceived;
        }

        private static void DisposeScope(UnifierTSL.Surface.Interactions.SurfaceInteractionScope? scope) {
            if (scope is null) {
                return;
            }

            try {
                scope.Dispose();
            }
            catch {
            }
        }

        #endregion

        #region Local Types

        private readonly record struct AdapterCommand(
            AdapterCommandKind Kind,
            ClientBufferedEditorState? BufferedState = null,
            SessionPublication? Publication = null);

        private readonly record struct BackgroundTaskDescriptor(
            BackgroundTaskKind Kind,
            long Serial);

        private sealed record PendingConsoleKeyRead(
            bool Intercept,
            TaskCompletionSource<ConsoleKeyInfo> Completion);

        private enum InteractionScopeMode : byte
        {
            None,
            Authoring,
            Blocking,
        }

        private enum AdapterCommandKind : byte
        {
            Submit,
            Publication,
            FormatEditor,
        }

        private enum BackgroundTaskKind : byte
        {
            Execution,
            ReturnValueTask,
        }

        #endregion
    }
}
