using Atelier.Session.Context;
using Atelier.Session.Execution;
using Atelier.Session.Roslyn;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;
using System.Threading.Channels;
using UnifierTSL.Contracts.Sessions;

namespace Atelier.Session
{
    internal readonly record struct DraftUpdateResult(bool SourceTextChanged);

    internal sealed class ReplSession : IDisposable, IAsyncDisposable
    {
        private readonly Lock sync = new();
        private readonly Channel<SessionCommand> queue = Channel.CreateUnbounded<SessionCommand>(new UnboundedChannelOptions {
            SingleReader = true,
            SingleWriter = false,
        });
        private readonly CancellationTokenSource disposeCancellation;
        private readonly ScriptHostContext hostContext;
        private readonly ScriptGlobals globals;
        private readonly ReplRuntime runtime;
        private readonly FrontendConfiguration frontendConfiguration;
        private readonly Task processorTask;
        private readonly Task<SessionPublication> warmupTask;
        private readonly ImportState baselineImportState;
        private readonly List<PendingExecution> pendingExecutions = [];

        private SessionRevision revision = SessionRevision.Empty;
        private ImmutableArray<CommittedSubmission> committedHistory = [];
        private WorkspaceAnalysis workspaceAnalysis = WorkspaceAnalysis.Empty;
        private RunResult lastRun = RunResult.Idle;
        private SessionPublication publication;
        private ImportState importState;
        private SessionInvalidation? invalidation;
        private AnalysisRequest? pendingAnalysisRequest;
        private CancellationTokenSource? runningAnalysisCancellation;
        private long draftGeneration;
        private long nextExecutionSerial = 1;
        private bool analysisWorkerRunning;
        private bool disposed;

        public ReplSession(
            SessionConfiguration configuration,
            ScriptHostContext hostContext,
            ManagedPluginAssemblyCatalog managedPluginCatalog,
            CancellationTokenSource disposeCancellation,
            ReplRuntime? runtime = null) {

            ArgumentNullException.ThrowIfNull(configuration);
            this.disposeCancellation = disposeCancellation ?? throw new ArgumentNullException(nameof(disposeCancellation));
            this.hostContext = hostContext ?? throw new ArgumentNullException(nameof(hostContext));
            this.globals = hostContext.Globals;
            this.runtime = runtime ?? new ReplRuntime(configuration, globals, managedPluginCatalog);
            frontendConfiguration = configuration.Frontend;
            ParseOptions = frontendConfiguration.ParseOptions;
            UseKAndRBraceStyle = frontendConfiguration.UseKAndRBraceStyle;
            UseSmartSubmitDetection = frontendConfiguration.UseSmartSubmitDetection;
            Console = globals.SessionConsole;
            baselineImportState = ImportState.CreateBaseline(frontendConfiguration.Imports, frontendConfiguration.ReferencePaths);
            importState = baselineImportState;
            publication = BuildPublicationLocked();
            processorTask = Task.Run(ProcessLoopAsync);
            warmupTask = RequestAnalysisAsync(this.disposeCancellation.Token).AsTask();
            UpdatePendingTasksSnapshot();
        }

        public event Action<SessionPublication>? PublicationChanged;

        public HostConsole Console { get; }

        public CSharpParseOptions ParseOptions { get; }

        public bool UseKAndRBraceStyle { get; }
        public bool UseSmartSubmitDetection { get; }

        public SessionPublication CurrentPublication {
            get {
                lock (sync) {
                    return publication;
                }
            }
        }

        public Task<SessionPublication> WarmupTask => warmupTask;

        public ImportState ImportState {
            get {
                lock (sync) {
                    return importState;
                }
            }
        }

        public ValueTask<DraftUpdateResult> UpdateDraftAsync(
            long clientBufferRevision,
            string draftText,
            int caretIndex,
            IReadOnlyList<ClientBufferedTextMarker>? markers = null,
            CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            var normalizedDraft = draftText ?? string.Empty;
            var normalizedMarkers = markers ?? [];
            var sourceTextChanged = false;
            lock (sync) {
                ObjectDisposedException.ThrowIf(disposed, this);
                var previousDraft = DraftMarkers.Decode(revision.DraftText, revision.Markers, revision.CaretIndex);
                var nextRevision = revision.WithDraft(clientBufferRevision, normalizedDraft, caretIndex, normalizedMarkers);
                var nextDraft = DraftMarkers.Decode(nextRevision.DraftText, nextRevision.Markers, nextRevision.CaretIndex);
                sourceTextChanged = !string.Equals(previousDraft.SourceText, nextDraft.SourceText, StringComparison.Ordinal);
                revision = nextRevision;
                if (sourceTextChanged) {
                    draftGeneration = checked(draftGeneration + 1);
                    workspaceAnalysis = WorkspaceAnalysis.Empty;
                }

                publication = BuildPublicationLocked();
            }

            return ValueTask.FromResult(new DraftUpdateResult(sourceTextChanged));
        }

        public ValueTask<SessionPublication> RequestAnalysisAsync(CancellationToken cancellationToken = default) {
            if (TryCaptureInvalidatedPublication(out var updatedPublication)) {
                return ValueTask.FromResult(updatedPublication);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var completion = CreateCompletionSource<SessionPublication>();
            QueueAnalysisRequest(CaptureAnalysisRequest(completion, cancellationToken));
            return new ValueTask<SessionPublication>(completion.Task);
        }

        public ValueTask<RunResult> QueuePersistentSubmitAsync(CancellationToken cancellationToken = default) {
            if (TryCreateInvalidatedRunResult(OperationKind.PersistentSubmit, out var runResult)) {
                return ValueTask.FromResult(runResult);
            }

            return EnqueueAsync(
                new PersistentSubmitCommand(CreateCompletionSource<RunResult>()),
                static command => command.Completion,
                cancellationToken);
        }

        public ValueTask<RunResult> QueueTransientRunAsync(string code, CancellationToken cancellationToken = default) {
            if (TryCreateInvalidatedRunResult(OperationKind.TransientRun, out var runResult)) {
                return ValueTask.FromResult(runResult);
            }

            return EnqueueAsync(
                new TransientRunCommand(code ?? string.Empty, CreateCompletionSource<RunResult>()),
                static command => command.Completion,
                cancellationToken);
        }

        public ValueTask<SessionPublication> ResetAsync(CancellationToken cancellationToken = default) {
            if (TryCaptureInvalidatedPublication(out var updatedPublication)) {
                return ValueTask.FromResult(updatedPublication);
            }

            return EnqueueAsync(
                new ResetCommand(CreateCompletionSource<SessionPublication>()),
                static command => command.Completion,
                cancellationToken);
        }

        public void Dispose() {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        public async ValueTask DisposeAsync() {
            Task? processorToAwait;
            CancellationTokenSource? runningAnalysisToCancel;
            AnalysisRequest? pendingAnalysisToCancel;
            lock (sync) {
                if (disposed) {
                    return;
                }

                disposed = true;
                processorToAwait = processorTask;
                runningAnalysisToCancel = runningAnalysisCancellation;
                runningAnalysisCancellation = null;
                pendingAnalysisToCancel = pendingAnalysisRequest;
                pendingAnalysisRequest = null;
            }

            queue.Writer.TryComplete();
            CancelAnalysis(runningAnalysisToCancel);
            CancelAnalysisRequest(pendingAnalysisToCancel);
            disposeCancellation.Cancel();

            try {
                await processorToAwait.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested) {
            }
            finally {
                CompletePendingCommands(new OperationCanceledException("Atelier REPL session disposed.", disposeCancellation.Token));
                globals.UpdatePendingTasks([]);
                disposeCancellation.Dispose();
                try {
                    runtime.Dispose();
                }
                finally {
                    hostContext.Dispose();
                }
            }
        }

        internal void InvalidateForManagedAssemblyChange(ImmutableArray<string> stableKeys, string reason) {
            if (stableKeys.IsDefaultOrEmpty || string.IsNullOrWhiteSpace(reason) || !UsesAnyManagedAssembly(stableKeys)) {
                return;
            }

            Action<SessionPublication>? changed;
            SessionPublication updatedPublication;
            CancellationTokenSource? runningAnalysisToCancel;
            AnalysisRequest? pendingAnalysisToCancel;
            lock (sync) {
                if (disposed || invalidation is not null) {
                    return;
                }

                invalidation = new SessionInvalidation(reason, DateTimeOffset.UtcNow);
                updatedPublication = BuildPublicationLocked();
                publication = updatedPublication;
                changed = PublicationChanged;
                runningAnalysisToCancel = runningAnalysisCancellation;
                runningAnalysisCancellation = null;
                pendingAnalysisToCancel = pendingAnalysisRequest;
                pendingAnalysisRequest = null;
            }

            CancelAnalysis(runningAnalysisToCancel);
            CancelAnalysisRequest(pendingAnalysisToCancel);
            runtime.Invalidate();
            changed?.Invoke(updatedPublication);
        }

        private static TaskCompletionSource<T> CreateCompletionSource<T>() {
            return new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private async ValueTask<T> EnqueueAsync<TCommand, T>(TCommand command, Func<TCommand, TaskCompletionSource<T>> getCompletion, CancellationToken cancellationToken)
            where TCommand : SessionCommand {
            ObjectDisposedException.ThrowIf(IsDisposed(), this);
            await queue.Writer.WriteAsync(command, cancellationToken).ConfigureAwait(false);
            return await getCompletion(command).Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        private bool IsDisposed() {
            lock (sync) {
                return disposed;
            }
        }

        private bool IsInvalidated() {
            lock (sync) {
                return invalidation is not null;
            }
        }

        private bool UsesAnyManagedAssembly(ImmutableArray<string> stableKeys) {
            if (stableKeys.IsDefaultOrEmpty) {
                return false;
            }

            try {
                var attachedKeys = runtime.AttachedManagedPluginKeys;
                return attachedKeys.Any(stableKeys.Contains);
            }
            catch (ObjectDisposedException) {
                return false;
            }
        }

        private bool TryCaptureInvalidatedPublication(out SessionPublication updatedPublication) {
            lock (sync) {
                if (invalidation is null) {
                    updatedPublication = null!;
                    return false;
                }

                updatedPublication = CaptureCurrentStateLocked();
                return true;
            }
        }

        private bool TryCreateInvalidatedRunResult(OperationKind operation, out RunResult runResult) {
            lock (sync) {
                if (invalidation is null) {
                    runResult = null!;
                    return false;
                }

                runResult = RunResult.Invalidated(operation, invalidation.Reason);
                return true;
            }
        }

        private async Task ProcessLoopAsync() {
            try {
                await foreach (var command in queue.Reader.ReadAllAsync(disposeCancellation.Token).ConfigureAwait(false)) {
                    await ExecuteCommandAsync(command, disposeCancellation.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested) {
                CompletePendingCommands(new OperationCanceledException("Atelier REPL session disposed.", disposeCancellation.Token));
            }
        }

        private async Task ExecuteCommandAsync(SessionCommand command, CancellationToken cancellationToken) {
            try {
                switch (command) {
                    case PersistentSubmitCommand submit when TryCreateInvalidatedRunResult(OperationKind.PersistentSubmit, out var invalidatedSubmit):
                        submit.Completion.TrySetResult(invalidatedSubmit);
                        break;

                    case PersistentSubmitCommand submit:
                        submit.Completion.TrySetResult(await ExecutePersistentSubmitAsync(cancellationToken).ConfigureAwait(false));
                        break;

                    case TransientRunCommand transient when TryCreateInvalidatedRunResult(OperationKind.TransientRun, out var invalidatedTransient):
                        transient.Completion.TrySetResult(invalidatedTransient);
                        break;

                    case TransientRunCommand transient:
                        transient.Completion.TrySetResult(await ExecuteTransientRunAsync(transient.Code, cancellationToken).ConfigureAwait(false));
                        break;

                    case BackgroundExecutionCompletedCommand completed:
                        ProcessBackgroundExecutionCompleted(completed.Serial, completed.Result);
                        break;

                    case ResetCommand reset when TryCaptureInvalidatedPublication(out var invalidatedPublication):
                        reset.Completion.TrySetResult(invalidatedPublication);
                        break;

                    case ResetCommand reset:
                        var resetRun = runtime.Reset();
                        lock (sync) {
                            lastRun = resetRun;
                            revision = revision.ResetSession();
                            committedHistory = [];
                            workspaceAnalysis = WorkspaceAnalysis.Empty;
                            importState = baselineImportState.ResetToBaseline();
                            draftGeneration = checked(draftGeneration + 1);
                        }

                        CancelCurrentAnalysis();
                        reset.Completion.TrySetResult(PublishCurrentState());
                        break;

                    default:
                        throw new InvalidOperationException(GetString($"Unsupported session command '{command.GetType().FullName}'."));
                }
            }
            catch (Exception ex) when (TryHandleCommandException(command, ex)) {
            }
        }

        private async Task<RunResult> ExecutePersistentSubmitAsync(CancellationToken cancellationToken) {
            var executionSerial = AllocateExecutionSerial();
            string submittedText;
            lock (sync) {
                submittedText = revision.BuildSyntheticDocument().DraftText;
                lastRun = RunResult.Pending(OperationKind.PersistentSubmit, executionSerial);
            }

            PublishCurrentState();

            var launch = await runtime.RunPersistentAsync(submittedText, cancellationToken).ConfigureAwait(false);
            var runResult = launch.RunResult.WithExecutionSerial(executionSerial);
            if (runResult.StateChanged) {
                lock (sync) {
                    committedHistory = committedHistory.Add(new CommittedSubmission(revision.CommittedRevision + 1, submittedText, DateTimeOffset.UtcNow));
                    revision = revision.AdvanceCommittedAndClearDraft();
                    workspaceAnalysis = WorkspaceAnalysis.Empty;
                    draftGeneration = checked(draftGeneration + 1);
                }

                CancelCurrentAnalysis();
            }

            if (launch.IsBackground) {
                var trackedCompletion = TrackBackgroundExecution(
                    executionSerial,
                    runResult.Operation,
                    isPersistent: true,
                    runResult.StateChanged,
                    launch.BackgroundCompletion!);
                var pendingRun = runResult.WithBackgroundExecution(executionSerial, isPersistent: true, trackedCompletion);
                lock (sync) {
                    lastRun = pendingRun;
                }

                if (!IsInvalidated()) {
                    await AnalyzeCurrentStateAsync(cancellationToken).ConfigureAwait(false);
                }

                PublishCurrentState();
                return pendingRun;
            }

            lock (sync) {
                lastRun = runResult;
            }

            if (!IsInvalidated()) {
                await AnalyzeCurrentStateAsync(cancellationToken).ConfigureAwait(false);
            }

            PublishCurrentState();
            return runResult;
        }

        private async Task<RunResult> ExecuteTransientRunAsync(string code, CancellationToken cancellationToken) {
            var executionSerial = AllocateExecutionSerial();
            lock (sync) {
                lastRun = RunResult.Pending(OperationKind.TransientRun, executionSerial);
            }

            PublishCurrentState();

            var launch = await runtime.RunTransientAsync(code, cancellationToken).ConfigureAwait(false);
            var runResult = launch.RunResult.WithExecutionSerial(executionSerial);
            if (launch.IsBackground) {
                var trackedCompletion = TrackBackgroundExecution(
                    executionSerial,
                    runResult.Operation,
                    isPersistent: false,
                    runResult.StateChanged,
                    launch.BackgroundCompletion!);
                var pendingRun = runResult.WithBackgroundExecution(executionSerial, isPersistent: false, trackedCompletion);
                lock (sync) {
                    lastRun = pendingRun;
                }

                PublishCurrentState();
                return pendingRun;
            }

            lock (sync) {
                lastRun = runResult;
            }

            if (!IsInvalidated()) {
                await AnalyzeCurrentStateAsync(cancellationToken).ConfigureAwait(false);
            }

            PublishCurrentState();
            return runResult;
        }

        private async Task AnalyzeCurrentStateAsync(CancellationToken cancellationToken) {
            var request = CaptureAnalysisRequest(completion: null, cancellationToken);
            try {
                var analysis = await AnalyzeRequestAsync(request).ConfigureAwait(false);
                ApplyAnalysisResult(request, analysis, publish: false);
            }
            finally {
                ReleaseAnalysisRequest(request);
            }
        }

        private AnalysisRequest CaptureAnalysisRequest(
            TaskCompletionSource<SessionPublication>? completion,
            CancellationToken cancellationToken) {
            var analysisCancellation = CancellationTokenSource.CreateLinkedTokenSource(disposeCancellation.Token, cancellationToken);
            try {
                lock (sync) {
                    ObjectDisposedException.ThrowIf(disposed, this);
                    return new AnalysisRequest(
                        draftGeneration,
                        committedHistory,
                        revision.BuildSyntheticDocument(),
                        analysisCancellation,
                        completion);
                }
            }
            catch {
                analysisCancellation.Dispose();
                throw;
            }
        }

        private async Task<WorkspaceAnalysis> AnalyzeRequestAsync(AnalysisRequest request) {
            using var workspace = new ReplWorkspace(frontendConfiguration);
            return await workspace.AnalyzeAsync(
                    request.CommittedHistory,
                    request.SyntheticDocument,
                    request.Cancellation.Token)
                .ConfigureAwait(false);
        }

        private SessionPublication ApplyAnalysisResult(AnalysisRequest request, WorkspaceAnalysis analysis, bool publish) {
            Action<SessionPublication>? changed = null;
            SessionPublication updatedPublication;
            lock (sync) {
                if (!disposed
                    && invalidation is null
                    && request.Generation == draftGeneration) {
                    workspaceAnalysis = analysis;
                    updatedPublication = BuildPublicationLocked();
                    publication = updatedPublication;
                    if (publish) {
                        changed = PublicationChanged;
                    }
                }
                else {
                    updatedPublication = publication;
                }
            }

            changed?.Invoke(updatedPublication);
            return updatedPublication;
        }

        private void QueueAnalysisRequest(AnalysisRequest request) {
            AnalysisRequest? supersededRequest;
            var startWorker = false;
            lock (sync) {
                ObjectDisposedException.ThrowIf(disposed, this);
                supersededRequest = pendingAnalysisRequest;
                pendingAnalysisRequest = request;
                if (!analysisWorkerRunning) {
                    analysisWorkerRunning = true;
                    startWorker = true;
                }
            }

            CancelAnalysisRequest(supersededRequest);
            if (startWorker) {
                _ = Task.Run(ProcessAnalysisRequestsAsync, CancellationToken.None);
            }
        }

        private async Task ProcessAnalysisRequestsAsync() {
            while (true) {
                AnalysisRequest? request;
                lock (sync) {
                    request = pendingAnalysisRequest;
                    pendingAnalysisRequest = null;
                    if (request is null) {
                        analysisWorkerRunning = false;
                        return;
                    }

                    runningAnalysisCancellation = request.Cancellation;
                }

                await RunQueuedAnalysisAsync(request).ConfigureAwait(false);
            }
        }

        private async Task RunQueuedAnalysisAsync(AnalysisRequest request) {
            try {
                var analysis = await AnalyzeRequestAsync(request).ConfigureAwait(false);
                request.Completion?.TrySetResult(ApplyAnalysisResult(request, analysis, publish: true));
            }
            catch (OperationCanceledException) when (request.Cancellation.IsCancellationRequested) {
                request.Completion?.TrySetCanceled(request.Cancellation.Token);
            }
            catch (ObjectDisposedException ex) {
                request.Completion?.TrySetException(ex);
            }
            catch (Exception ex) {
                request.Completion?.TrySetException(ex);
            }
            finally {
                ReleaseAnalysisRequest(request);
            }
        }

        private void ReleaseAnalysisRequest(AnalysisRequest request) {
            lock (sync) {
                if (ReferenceEquals(runningAnalysisCancellation, request.Cancellation)) {
                    runningAnalysisCancellation = null;
                }
            }

            request.Cancellation.Dispose();
        }

        private void CancelCurrentAnalysis() {
            CancellationTokenSource? runningAnalysisToCancel;
            AnalysisRequest? pendingAnalysisToCancel;
            lock (sync) {
                runningAnalysisToCancel = runningAnalysisCancellation;
                runningAnalysisCancellation = null;
                pendingAnalysisToCancel = pendingAnalysisRequest;
                pendingAnalysisRequest = null;
            }

            CancelAnalysis(runningAnalysisToCancel);
            CancelAnalysisRequest(pendingAnalysisToCancel);
        }

        private static void CancelAnalysis(CancellationTokenSource? cancellation) {
            if (cancellation is null) {
                return;
            }

            try {
                cancellation.Cancel();
            }
            catch (ObjectDisposedException) {
            }
        }

        private static void CancelAnalysisRequest(AnalysisRequest? request) {
            if (request is null) {
                return;
            }

            CancelAnalysis(request.Cancellation);
            request.Completion?.TrySetCanceled(request.Cancellation.Token);
            request.Cancellation.Dispose();
        }

        private Task<RunResult> TrackBackgroundExecution(
            long executionSerial,
            OperationKind operation,
            bool isPersistent,
            bool stateChanged,
            Task<RunResult> completionTask) {

            var trackedTask = AwaitTrackedBackgroundExecutionAsync(executionSerial, operation, stateChanged, completionTask);
            pendingExecutions.Add(new PendingExecution(executionSerial, operation, isPersistent, trackedTask));
            UpdatePendingTasksSnapshot();
            return trackedTask;
        }

        private async Task<RunResult> AwaitTrackedBackgroundExecutionAsync(
            long executionSerial,
            OperationKind operation,
            bool stateChanged,
            Task<RunResult> completionTask) {
            RunResult result;
            try {
                result = await completionTask.ConfigureAwait(false);
            }
            catch (Exception ex) {
                result = CreateUnexpectedBackgroundFailureResult(operation, stateChanged, ex);
            }

            var normalized = result.WithExecutionSerial(executionSerial);
            queue.Writer.TryWrite(new BackgroundExecutionCompletedCommand(executionSerial, normalized));
            return normalized;
        }

        private void ProcessBackgroundExecutionCompleted(long executionSerial, RunResult runResult) {
            RemovePendingExecution(executionSerial);
            var shouldPublish = false;
            lock (sync) {
                if (invalidation is not null) {
                    return;
                }

                if (lastRun.ExecutionSerial != executionSerial
                    || lastRun.Outcome != OutcomeKind.Pending
                    || lastRun.ExecutionPhase != ExecutionPhase.Background) {
                    return;
                }

                lastRun = runResult;
                shouldPublish = true;
            }

            if (shouldPublish) {
                PublishCurrentState();
            }
        }

        private void RemovePendingExecution(long executionSerial) {
            for (var index = 0; index < pendingExecutions.Count; index++) {
                if (pendingExecutions[index].Serial != executionSerial) {
                    continue;
                }

                pendingExecutions.RemoveAt(index);
                UpdatePendingTasksSnapshot();
                return;
            }
        }

        private void UpdatePendingTasksSnapshot() {
            globals.UpdatePendingTasks([.. pendingExecutions.Select(static execution => (Task)execution.CompletionTask)]);
        }

        private long AllocateExecutionSerial() {
            return nextExecutionSerial++;
        }

        private SessionPublication PublishCurrentState() {
            Action<SessionPublication>? changed;
            SessionPublication updatedPublication;
            lock (sync) {
                updatedPublication = BuildPublicationLocked();
                publication = updatedPublication;
                changed = PublicationChanged;
            }

            changed?.Invoke(updatedPublication);
            return updatedPublication;
        }

        private SessionPublication CaptureCurrentState() {
            lock (sync) {
                return CaptureCurrentStateLocked();
            }
        }

        private SessionPublication CaptureCurrentStateLocked() {
            var updatedPublication = BuildPublicationLocked();
            publication = updatedPublication;
            return updatedPublication;
        }

        private SessionPublication BuildPublicationLocked() {
            var syntheticDocument = revision.BuildSyntheticDocument();
            return new SessionPublication(
                revision,
                committedHistory,
                syntheticDocument,
                workspaceAnalysis,
                invalidation,
                revision.DraftText);
        }

        private static bool TryHandleCommandException(SessionCommand command, Exception exception) {
            switch (command) {
                case PersistentSubmitCommand submit:
                    return submit.Completion.TrySetException(exception);

                case TransientRunCommand transient:
                    return transient.Completion.TrySetException(exception);

                case ResetCommand reset:
                    return reset.Completion.TrySetException(exception);

                default:
                    return false;
            }
        }

        private static RunResult CreateUnexpectedBackgroundFailureResult(
            OperationKind operation,
            bool stateChanged,
            Exception exception) {
            return new RunResult(
                operation,
                OutcomeKind.RuntimeFailed,
                ExecutionPhase.None,
                [],
                null,
                false,
                exception,
                stateChanged,
                0,
                null,
                DateTimeOffset.UtcNow);
        }

        private void CompletePendingCommands(Exception exception) {
            while (queue.Reader.TryRead(out var command)) {
                TryHandleCommandException(command, exception);
            }
        }

        private abstract record SessionCommand;

        private sealed record PersistentSubmitCommand(TaskCompletionSource<RunResult> Completion) : SessionCommand;

        private sealed record TransientRunCommand(string Code, TaskCompletionSource<RunResult> Completion) : SessionCommand;

        private sealed record BackgroundExecutionCompletedCommand(long Serial, RunResult Result) : SessionCommand;

        private sealed record ResetCommand(TaskCompletionSource<SessionPublication> Completion) : SessionCommand;

        private sealed record PendingExecution(
            long Serial,
            OperationKind Operation,
            bool IsPersistent,
            Task<RunResult> CompletionTask);

        private sealed record AnalysisRequest(
            long Generation,
            ImmutableArray<CommittedSubmission> CommittedHistory,
            SyntheticDocument SyntheticDocument,
            CancellationTokenSource Cancellation,
            TaskCompletionSource<SessionPublication>? Completion);
    }
}
