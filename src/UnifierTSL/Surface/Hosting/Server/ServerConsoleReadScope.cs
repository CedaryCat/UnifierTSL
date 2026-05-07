using UnifierTSL.Contracts.Projection;
using UnifierTSL.Contracts.Sessions;
using UnifierTSL.Contracts.Terminal;
using UnifierTSL.Surface.Prompting;
using UnifierTSL.Surface.Prompting.Sessions;
using UnifierTSL.Surface.Interactions;

namespace UnifierTSL.Surface.Hosting.Server;

internal sealed class ServerConsoleReadScope(
    ISurfaceSession session,
    Func<PromptSurfaceSpec> defaultPromptSpecFactory,
    Action<string?> write,
    Action<string?> writeLine) : IDisposable
{
    private sealed class SubmittedReadState
    {
        public TaskCompletionSource<string> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Completed;
    }

    private sealed class KeyReadState
    {
        public TaskCompletionSource<ConsoleKeyInfo> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Completed;
    }

    private readonly ISurfaceSession session = session ?? throw new ArgumentNullException(nameof(session));
    private readonly Func<PromptSurfaceSpec> defaultPromptSpecFactory = defaultPromptSpecFactory ?? throw new ArgumentNullException(nameof(defaultPromptSpecFactory));
    private readonly Action<string?> write = write ?? throw new ArgumentNullException(nameof(write));
    private readonly Action<string?> writeLine = writeLine ?? throw new ArgumentNullException(nameof(writeLine));
    private readonly Lock stateLock = new();
    private readonly SemaphoreSlim readGate = new(1, 1);
    private readonly Queue<char> bufferedReadChars = [];

    private bool disposed;
    private SubmittedReadState? currentSubmittedRead;
    private KeyReadState? currentReadKey;

    public string ReadLine() {
        return ExecuteReadLine();
    }

    public string ReadLine(PromptSurfaceSpec prompt) {
        return ExecuteReadLine(prompt);
    }

    public ConsoleKeyInfo ReadKey(bool intercept) {
        return ExecuteReadKey(intercept);
    }

    public int Read() {
        return ExecuteRead();
    }

    public void Dispose() {
        SubmittedReadState? submittedReadToCancel;
        KeyReadState? keyReadToCancel;
        lock (stateLock) {
            if (disposed) {
                return;
            }

            disposed = true;
            submittedReadToCancel = currentSubmittedRead;
            keyReadToCancel = currentReadKey;
            currentSubmittedRead = null;
            currentReadKey = null;
            bufferedReadChars.Clear();
        }

        submittedReadToCancel?.Completion.TrySetException(new ObjectDisposedException(nameof(ServerConsoleReadScope)));
        keyReadToCancel?.Completion.TrySetException(new ObjectDisposedException(nameof(ServerConsoleReadScope)));
    }

    private string ExecuteReadLine(PromptSurfaceSpec? promptSpec = null) {
        ThrowIfDisposed();
        readGate.Wait();
        try {
            var effectivePromptSpec = promptSpec ?? defaultPromptSpecFactory();

            var authoring = effectivePromptSpec.BufferedAuthoring;
            var promptDriverOptions = new ClientBufferedPromptInteractionDriverOptions(
                PromptInteractionRunner.PagedRenderOptions,
                authoring);
            return ExecuteSubmittedReadCore(
                SurfaceInteractionKinds.AuthoringReadLine,
                effectivePromptSpec,
                "read line",
                promptDriverOptions: promptDriverOptions);
        }
        finally {
            readGate.Release();
        }
    }

    private int ExecuteRead() {
        ThrowIfDisposed();
        readGate.Wait();
        try {
            if (!TryReadBufferedChar(out var value)) {
                ExecuteSubmittedReadCore(
                    SurfaceInteractionKinds.AuthoringRead,
                    CreatePlainReadPromptSpec(),
                    "read",
                    onSubmitted: submittedText => {
                        EchoSubmittedLine(submittedText);
                        EnqueueSubmittedLine(submittedText);
                    });
                if (!TryReadBufferedChar(out value)) {
                    throw new InvalidOperationException(GetString("Surface session read did not produce a buffered character."));
                }
            }

            return value;
        }
        finally {
            readGate.Release();
        }
    }

    private ConsoleKeyInfo ExecuteReadKey(bool intercept) {
        ThrowIfDisposed();
        readGate.Wait();
        using var scope = new AuthoringSurfaceInteractionScope(
            session,
            new SurfaceInteractionScopeOptions(
                SurfaceInteractionKinds.AuthoringReadKey,
                IsTransient: true));
        using var promptSession = new ClientBufferedPromptInteractionSession(
            ClientBufferedPromptInteractionSessionOptions.CreateWindowed(
                CreatePlainReadPromptSpec(captureRawKeys: true),
                SurfaceInteractionKinds.AuthoringReadKey));
        var waiting = new KeyReadState();
        void OnBufferedState(ClientBufferedEditorState state) {
            try {
                promptSession.PublishBufferedState(state);
            }
            catch (ObjectDisposedException) {
            }
        }

        void OnPromptStateChanged(ClientBufferedPromptInteractionState _) {
            try {
                scope.PublishDocument(promptSession.CreateDocument(scope.Scope.Id));
            }
            catch (ObjectDisposedException) {
            }
        }

        void OnKeyReceived(ConsoleKeyInfo keyInfo) {
            lock (stateLock) {
                if (!ReferenceEquals(currentReadKey, waiting) || waiting.Completed) {
                    return;
                }

                waiting.Completed = true;
            }

            if (!intercept) {
                EchoKey(keyInfo);
            }

            promptSession.SetRuntimeRefreshEnabled(false);
            scope.Complete();
            waiting.Completion.TrySetResult(keyInfo);
        }

        void OnTerminated(SurfaceInteractionTermination termination) {
            lock (stateLock) {
                if (!ReferenceEquals(currentReadKey, waiting) || waiting.Completed) {
                    return;
                }

                waiting.Completed = true;
            }

            waiting.Completion.TrySetException(CreateReadTerminationException("read key", termination));
        }

        scope.EditorStateSyncReceived += OnBufferedState;
        scope.SubmitReceived += OnBufferedState;
        promptSession.StateChanged += OnPromptStateChanged;
        scope.KeyReceived += OnKeyReceived;
        scope.Terminated += OnTerminated;
        scope.PublishDocument(promptSession.CreateDocument(scope.Scope.Id));
        try {
            lock (stateLock) {
                ThrowIfDisposed();
                currentReadKey = waiting;
            }

            promptSession.SetRuntimeRefreshEnabled(true);
            scope.Start();
            return waiting.Completion.Task.GetAwaiter().GetResult();
        }
        finally {
            scope.KeyReceived -= OnKeyReceived;
            scope.Terminated -= OnTerminated;
            scope.EditorStateSyncReceived -= OnBufferedState;
            scope.SubmitReceived -= OnBufferedState;
            promptSession.StateChanged -= OnPromptStateChanged;
            promptSession.SetRuntimeRefreshEnabled(false);

            lock (stateLock) {
                if (ReferenceEquals(currentReadKey, waiting)) {
                    currentReadKey = null;
                }
            }

            readGate.Release();
        }
    }

    private string ExecuteSubmittedReadCore(
        string interactionKind,
        PromptSurfaceSpec promptSpec,
        string operationName,
        Action<string>? onSubmitted = null,
        ClientBufferedPromptInteractionDriverOptions? promptDriverOptions = null) {
        using var scope = new AuthoringSurfaceInteractionScope(
            session,
            new SurfaceInteractionScopeOptions(
                interactionKind,
                IsTransient: true));
        using var promptSession = new ClientBufferedPromptInteractionSession(
            ClientBufferedPromptInteractionSessionOptions.CreateWindowed(
                promptSpec,
                interactionKind,
                promptDriverOptions));
        var waiting = new SubmittedReadState();
        void OnBufferedState(ClientBufferedEditorState state) {
            try {
                promptSession.PublishBufferedState(state);
            }
            catch (ObjectDisposedException) {
            }
        }

        void OnPromptStateChanged(ClientBufferedPromptInteractionState _) {
            try {
                scope.PublishDocument(promptSession.CreateDocument(scope.Scope.Id));
            }
            catch (ObjectDisposedException) {
            }
        }

        void OnSubmitted(ClientBufferedEditorState bufferedState) {
            string submittedText;
            lock (stateLock) {
                if (!ReferenceEquals(currentSubmittedRead, waiting) || waiting.Completed) {
                    return;
                }

                waiting.Completed = true;
                submittedText = bufferedState.BufferText ?? string.Empty;
            }

            onSubmitted?.Invoke(submittedText);
            promptSession.SetRuntimeRefreshEnabled(false);
            scope.Complete();
            waiting.Completion.TrySetResult(submittedText);
        }

        void OnTerminated(SurfaceInteractionTermination termination) {
            lock (stateLock) {
                if (!ReferenceEquals(currentSubmittedRead, waiting) || waiting.Completed) {
                    return;
                }

                waiting.Completed = true;
            }

            waiting.Completion.TrySetException(CreateReadTerminationException(operationName, termination));
        }

        scope.EditorStateSyncReceived += OnBufferedState;
        scope.SubmitReceived += OnBufferedState;
        promptSession.StateChanged += OnPromptStateChanged;
        scope.SubmitReceived += OnSubmitted;
        scope.Terminated += OnTerminated;
        scope.PublishDocument(promptSession.CreateDocument(scope.Scope.Id));
        try {
            lock (stateLock) {
                ThrowIfDisposed();
                currentSubmittedRead = waiting;
            }

            promptSession.SetRuntimeRefreshEnabled(true);
            scope.Start();
            return waiting.Completion.Task.GetAwaiter().GetResult();
        }
        finally {
            scope.SubmitReceived -= OnSubmitted;
            scope.Terminated -= OnTerminated;
            scope.EditorStateSyncReceived -= OnBufferedState;
            scope.SubmitReceived -= OnBufferedState;
            promptSession.StateChanged -= OnPromptStateChanged;
            promptSession.SetRuntimeRefreshEnabled(false);

            lock (stateLock) {
                if (ReferenceEquals(currentSubmittedRead, waiting)) {
                    currentSubmittedRead = null;
                }
            }
        }
    }

    private bool TryReadBufferedChar(out int value) {
        lock (stateLock) {
            if (bufferedReadChars.Count == 0) {
                value = -1;
                return false;
            }

            value = bufferedReadChars.Dequeue();
            return true;
        }
    }

    private void EnqueueSubmittedLine(string submittedText) {
        var normalized = submittedText ?? string.Empty;
        lock (stateLock) {
            foreach (var ch in normalized) {
                bufferedReadChars.Enqueue(ch);
            }

            foreach (var ch in Environment.NewLine) {
                bufferedReadChars.Enqueue(ch);
            }
        }
    }

    private void EchoSubmittedLine(string submittedText) {
        try {
            writeLine(submittedText ?? string.Empty);
        }
        catch {
        }
    }

    private void EchoKey(ConsoleKeyInfo keyInfo) {
        try {
            if (keyInfo.KeyChar is '\r' or '\n') {
                writeLine(string.Empty);
                return;
            }

            if (keyInfo.KeyChar == '\0' || char.IsControl(keyInfo.KeyChar)) {
                return;
            }

            write(keyInfo.KeyChar.ToString());
        }
        catch {
        }
    }

    private static PromptSurfaceSpec CreatePlainReadPromptSpec(bool captureRawKeys = false) {
        var bufferedAuthoring = new PromptBufferedAuthoringOptions {
            Keymap = PromptEditorKeymaps.CreateSingleLine(),
            AuthoringBehavior = new EditorAuthoringBehavior {
                OpensCompletionAutomatically = false,
                CapturesRawKeys = captureRawKeys,
            },
        };
        return new PromptSurfaceSpec {
            Content = PromptProjectionDocumentFactory.CreateContent(
                PromptProjectionDocumentFactory.CreateRenderOptions(bufferedAuthoring),
                PromptProjectionDocumentFactory.CreateProjectionStyleDictionary(null),
                nodes: [
                    new TextProjectionNodeState {
                        NodeId = PromptProjectionDocumentFactory.NodeIds.Label,
                        State = new TextNodeState {
                            Content = PromptProjectionDocumentFactory.CreateSingleLineBlock(string.Empty),
                        },
                    },
                ]),
            BufferedAuthoring = bufferedAuthoring,
        };
    }

    private static Exception CreateReadTerminationException(
        string operationName,
        SurfaceInteractionTermination termination) {
        return new OperationCanceledException(
            $"Surface session authoring {operationName} ended with phase '{termination.Phase}'.");
    }

    private void ThrowIfDisposed() {
        ObjectDisposedException.ThrowIf(disposed, this);
    }
}
