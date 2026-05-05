using UnifierTSL.Contracts.Projection;
using UnifierTSL.Contracts.Projection.BuiltIn;
using UnifierTSL.Contracts.Protocol.Payloads;
using UnifierTSL.Contracts.Sessions;
using UnifierTSL.Surface.Hosting;

namespace UnifierTSL.Surface.Adapter.Cli.Sessions {
    internal sealed class PipeSurfaceSessionDriver : ISurfaceSession {
        private sealed class ActiveInteractionState {
            public required InteractionScope Scope;
            public bool StartRequested;
            public bool StartPublished;
        }

        private sealed class RememberedSurfaceHostState {
            private SurfaceHostPropertiesPatchOperation surfaceHostProperties = new();

            public void Remember(SurfaceHostOperation operation) {
                if (!SurfaceHostOperations.TryGetProperties(operation, out var properties)
                    || !SurfaceHostOperations.HasProperties(properties)) {
                    return;
                }

                surfaceHostProperties = SurfaceHostOperations.MergeProperties(surfaceHostProperties, properties);
            }

            public SurfaceHostOperation? Snapshot() {
                return !SurfaceHostOperations.HasProperties(surfaceHostProperties)
                    ? null
                    : SurfaceHostOperations.PropertiesPatch(surfaceHostProperties);
            }
        }

        private readonly Lock stateLock = new();
        private readonly string pipeName;
        private readonly SurfaceClientProcessLauncher clientLauncher;
        private readonly PipeSurfaceSessionConnection connection;
        private readonly SurfaceSessionLifetimeMode lifetimeMode;
        private readonly RememberedSurfaceHostState rememberedSurfaceHostState = new();

        private ActiveInteractionState? activeInteraction;
        private long bootstrapRevision;
        private long nextSurfaceHostOperationSequence;
        private long nextSurfaceOperationSequence;
        private long nextStreamSequence;
        private long nextLifecycleSequence;
        private long nextInputSequence;
        private long nextCompletionSequence;
        private long nextProjectionDocumentSequence;
        private long nextProjectionDefinitionRevision;
        private long nextProjectionStateRevision;
        private long nextProjectionStyleRevision = 0;
        private string projectionStyleSignature = string.Empty;
        private bool started;
        private bool disposed;

        public PipeSurfaceSessionDriver(SurfaceClientProcessLauncher clientLauncher, SurfaceSessionOptions options) {
            this.clientLauncher = clientLauncher ?? throw new ArgumentNullException(nameof(clientLauncher));
            ArgumentNullException.ThrowIfNull(options);
            pipeName = clientLauncher.PipeName;
            connection = new PipeSurfaceSessionConnection(pipeName);
            lifetimeMode = options.LifetimeMode;
        }

        public event Action? PresentationAttached;
        public event Action? PresentationDetached;
        public event Action? ReleaseRequested;
        public event Action<int>? ActivitySelectionRequested;
        public event Action<InputEventPayload>? InputReceived;
        public event Action<LifecyclePayload>? LifecycleReceived;
        public event Action<SurfaceCompletionPayload>? CompletionReceived;

        public bool IsPresentationAttached => connection.IsConnected;

        public void Start() {
            lock (stateLock) {
                if (started || disposed) {
                    return;
                }

                started = true;
                var sessionThread = new Thread(CommunicationLoop) {
                    IsBackground = true,
                    Name = $"SurfaceSession:{pipeName}"
                };
                sessionThread.Start();
            }
        }

        public void PublishSurfaceHostOperation(SurfaceHostOperation operation) {
            SurfaceHostOperationPayload payload;
            lock (stateLock) {
                ThrowIfDisposed();
                rememberedSurfaceHostState.Remember(operation);
                payload = CreateSurfaceHostOperationPayloadLocked(operation);
            }

            connection.SendPayload(payload);
        }

        public void PublishProjectionSnapshot(ProjectionSnapshotPayload snapshot) {
            PreparedSurfacePublication? publication;
            lock (stateLock) {
                ThrowIfDisposed();
                publication = PrepareProjectionPublicationLocked(snapshot);
            }

            PublishPreparedSurfacePublication(publication);
        }

        public void PublishSurfaceOperation(SurfaceOperation operation) {
            PreparedSurfacePublication? publication;
            lock (stateLock) {
                ThrowIfDisposed();
                publication = PrepareSurfacePublicationLocked(operation);
            }

            PublishPreparedSurfacePublication(publication);
        }

        public InteractionScope OpenInteractionScope(
            string interactionKind,
            bool isTransient = true) {
            ArgumentException.ThrowIfNullOrWhiteSpace(interactionKind);
            lock (stateLock) {
                ThrowIfDisposed();
                if (activeInteraction is not null) {
                    throw new InvalidOperationException(GetString("The surface session already has an active interaction scope."));
                }

                var scope = new InteractionScope {
                    Id = InteractionScopeId.New(),
                    State = InteractionScopeState.Active,
                    Kind = interactionKind,
                    IsTransient = isTransient,
                };
                activeInteraction = new ActiveInteractionState {
                    Scope = scope,
                };
                return scope;
            }
        }

        public void Dispose() {
            lock (stateLock) {
                if (disposed) {
                    return;
                }

                disposed = true;
            }

            connection.Dispose();
            clientLauncher.Dispose();
        }

        private void CommunicationLoop() {
            while (true) {
                if (disposed) {
                    return;
                }

                var clientWasAttached = false;
                var releaseRequested = false;
                SurfaceHostOperation? replaySurfaceHostOperation = null;
                try {
                    connection.OpenListener();
                    clientLauncher.RestartClient();
                    connection.WaitForConnection();
                    clientWasAttached = true;
                    lock (stateLock) {
                        replaySurfaceHostOperation = rememberedSurfaceHostState.Snapshot();
                    }

                    SendBootstrap();
                    if (replaySurfaceHostOperation is { } rememberedSurfaceHostOperation) {
                        SurfaceHostOperationPayload payload;
                        lock (stateLock) {
                            if (disposed) {
                                return;
                            }

                            payload = CreateSurfaceHostOperationPayloadLocked(rememberedSurfaceHostOperation);
                        }

                        connection.SendPayload(payload);
                    }

                    PublishPendingInteractionStart();
                    PresentationAttached?.Invoke();
                    connection.Listen(HandleIncomingPayload);
                }
                catch {
                    if (disposed) {
                        return;
                    }
                }
                finally {
                    connection.Reset();
                    clientLauncher.StopClient(killIfRunning: true);
                    if (clientWasAttached) {
                        MarkActiveInteractionDetached();
                        PresentationDetached?.Invoke();
                        releaseRequested = lifetimeMode == SurfaceSessionLifetimeMode.UserReleasable;
                    }
                }

                if (disposed) {
                    return;
                }

                if (releaseRequested) {
                    ReleaseRequested?.Invoke();
                    Dispose();
                    return;
                }

                Thread.Sleep(3000);
            }
        }

        private void SendBootstrap() {
            BootstrapPayload payload;
            lock (stateLock) {
                bootstrapRevision += 1;
                payload = new BootstrapPayload {
                    Bootstrap = new SurfaceBootstrap {
                        Revisions = new SurfaceRevisionSet {
                            BootstrapRevision = bootstrapRevision,
                        },
                    },
                };
            }

            connection.SendPayload(payload);
        }

        private void PublishPendingInteractionStart() {
            PreparedSurfacePublication? publication;
            lock (stateLock) {
                if (activeInteraction is not { StartRequested: true, StartPublished: false } active) {
                    return;
                }

                active.StartPublished = true;
                publication = CreateLifecyclePublicationLocked(CreateLifecyclePayload(
                    active.Scope,
                    LifecyclePhase.Active));
            }

            PublishPreparedSurfacePublication(publication);
        }

        private void MarkActiveInteractionDetached() {
            lock (stateLock) {
                if (activeInteraction is { } active) {
                    active.StartPublished = false;
                }
            }
        }

        private void HandleIncomingPayload(ISurfacePayload payload) {
            InputEventPayload? inputPayload = null;
            int? activitySelectionDelta = null;
            LifecyclePayload? lifecyclePayload = null;
            SurfaceCompletionPayload? completionPayload = null;
            LifecyclePayload? completedLifecyclePayload = null;

            lock (stateLock) {
                if (disposed) {
                    return;
                }

                switch (payload) {
                    case InputEventPayload input:
                        inputPayload = AcceptInputPayload(input);
                        break;

                    case LifecyclePayload lifecycle:
                        lifecyclePayload = AcceptLifecyclePayload(lifecycle);
                        break;

                    case SurfaceCompletionPayload completion:
                        var completionAcceptance = AcceptCompletionPayload(completion);
                        completionPayload = completionAcceptance?.Completion;
                        completedLifecyclePayload = completionAcceptance?.Lifecycle;
                        break;
                }
            }

            if (inputPayload?.Event.Command == InteractionCommandIds.ActivitySelectRelative) {
                activitySelectionDelta = inputPayload.Event.Delta;
                inputPayload = null;
            }

            if (activitySelectionDelta.HasValue) {
                ActivitySelectionRequested?.Invoke(activitySelectionDelta.Value);
            }

            if (inputPayload is not null) {
                InputReceived?.Invoke(inputPayload);
            }

            if (lifecyclePayload is not null) {
                LifecycleReceived?.Invoke(lifecyclePayload);
            }

            if (completedLifecyclePayload is not null) {
                PublishLifecycle(completedLifecyclePayload);
            }

            if (completionPayload is not null) {
                CompletionReceived?.Invoke(completionPayload);
            }
        }

        private PreparedSurfacePublication? PrepareSurfacePublicationLocked(SurfaceOperation operation) {
            if (SurfaceOperations.TryGetStream(operation, out var streamPayload)) {
                return CreatePreparedSurfacePublicationLocked(SurfaceOperations.Stream(StampStreamPayloadLocked(streamPayload)));
            }

            if (SurfaceOperations.TryGetLifecycle(operation, out var lifecyclePayload)) {
                return PrepareLifecycleRequestLocked(lifecyclePayload);
            }

            return CreatePreparedSurfacePublicationLocked(operation);
        }

        private PreparedSurfacePublication? PrepareProjectionPublicationLocked(ProjectionSnapshotPayload snapshot) {
            var scope = ProjectionDocumentOps.GetScope(snapshot);
            if (SessionStatusProjectionSchema.IsSessionStatusScope(scope)) {
                return CreateProjectionSnapshotPublicationLocked(snapshot);
            }

            if (scope.Kind != ProjectionScopeKind.Interaction || string.IsNullOrWhiteSpace(scope.ScopeId)) {
                throw new InvalidOperationException(
                    GetString($"Surface session publication only accepts session-status or interaction projection scopes. Actual: {scope.Kind}/{scope.DocumentKind}."));
            }

            if (activeInteraction is not { } active || active.Scope.Id.Value != scope.ScopeId) {
                return null;
            }

            ValidateInteractionScope(active.Scope, scope);
            return CreateProjectionSnapshotPublicationLocked(snapshot);
        }

        private PreparedSurfacePublication? PrepareLifecycleRequestLocked(LifecyclePayload payload) {
            if (payload.InteractionScopeId is not { } scopeId) {
                return CreateLifecyclePublicationLocked(payload);
            }

            if (payload.Phase == LifecyclePhase.Active) {
                if (activeInteraction is not { } active || active.Scope.Id != scopeId) {
                    return null;
                }

                active.StartRequested = true;
                if (!connection.IsConnected || active.StartPublished) {
                    return null;
                }

                active.StartPublished = true;
                return CreateLifecyclePublicationLocked(CreateLifecyclePayload(
                    active.Scope,
                    LifecyclePhase.Active));
            }

            if (payload.Phase is LifecyclePhase.Completed or LifecyclePhase.Released) {
                if (activeInteraction is not { } active || active.Scope.Id != scopeId) {
                    return null;
                }

                activeInteraction = null;
                return CreateLifecyclePublicationLocked(CreateLifecyclePayload(
                    active.Scope,
                    payload.Phase));
            }

            return CreateLifecyclePublicationLocked(payload);
        }

        private void PublishLifecycle(LifecyclePayload payload) {
            PreparedSurfacePublication publication;
            lock (stateLock) {
                if (disposed) {
                    return;
                }

                publication = CreateLifecyclePublicationLocked(payload);
            }

            PublishPreparedSurfacePublication(publication);
        }

        private void PublishPreparedSurfacePublication(PreparedSurfacePublication? publication) {
            if (publication is not { } ready) {
                return;
            }

            connection.SendPayload(ready.Payload);
            if (ready.Lifecycle is not null) {
                LifecycleReceived?.Invoke(ready.Lifecycle);
            }
        }

        private PreparedSurfacePublication CreatePreparedSurfacePublicationLocked(
            SurfaceOperation operation,
            LifecyclePayload? lifecycle = null) {
            return new PreparedSurfacePublication(
                new SurfaceOperationPayload {
                    OperationSequence = ++nextSurfaceOperationSequence,
                    Operation = operation,
                },
                lifecycle);
        }

        private PreparedSurfacePublication CreateProjectionSnapshotPublicationLocked(ProjectionSnapshotPayload snapshot) {
            return new PreparedSurfacePublication(
                new ProjectionSnapshotOperationPayload {
                    OperationSequence = ++nextSurfaceOperationSequence,
                    Snapshot = StampProjectionSnapshotLocked(snapshot),
                },
                null);
        }

        private PreparedSurfacePublication CreateLifecyclePublicationLocked(LifecyclePayload payload) {
            var stampedPayload = StampLifecyclePayloadLocked(payload);
            return CreatePreparedSurfacePublicationLocked(
                SurfaceOperations.Lifecycle(stampedPayload),
                stampedPayload);
        }

        private SurfaceHostOperationPayload CreateSurfaceHostOperationPayloadLocked(SurfaceHostOperation operation) {
            return new SurfaceHostOperationPayload {
                OperationSequence = ++nextSurfaceHostOperationSequence,
                Operation = operation,
            };
        }

        private StreamPayload StampStreamPayloadLocked(StreamPayload payload) {
            return new StreamPayload {
                StreamSequence = ++nextStreamSequence,
                Kind = payload.Kind,
                Channel = payload.Channel,
                Text = payload.Text,
                IsAnsi = payload.IsAnsi,
                StyledText = payload.StyledText,
                Styles = payload.Styles,
            };
        }

        private LifecyclePayload StampLifecyclePayloadLocked(LifecyclePayload payload) {
            return new LifecyclePayload {
                InteractionScopeId = payload.InteractionScopeId,
                LifecycleSequence = ++nextLifecycleSequence,
                Phase = payload.Phase,
                InteractionKind = payload.InteractionKind,
                IsTransient = payload.IsTransient,
            };
        }

        private ProjectionSnapshotPayload StampProjectionSnapshotLocked(ProjectionSnapshotPayload payload) {
            nextProjectionDefinitionRevision = Math.Max(1, nextProjectionDefinitionRevision + 1);
            nextProjectionStateRevision += 1;
            var styleSignature = ResolveProjectionStyleSignature(payload);
            if (styleSignature is not null
                && !string.Equals(projectionStyleSignature, styleSignature, StringComparison.Ordinal)) {
                projectionStyleSignature = styleSignature;
                nextProjectionStyleRevision += 1;
            }

            return new ProjectionSnapshotPayload {
                Revisions = new ProjectionRevisionSet {
                    BootstrapRevision = bootstrapRevision,
                    DefinitionRevision = nextProjectionDefinitionRevision,
                    StateRevision = nextProjectionStateRevision,
                    StyleRevision = nextProjectionStyleRevision,
                },
                Sequences = new ProjectionSequenceSet {
                    LifecycleSequence = nextLifecycleSequence,
                    DocumentSequence = ++nextProjectionDocumentSequence,
                    StreamSequence = nextStreamSequence,
                    InputSequence = nextInputSequence,
                    CompletionSequence = nextCompletionSequence,
                },
                Body = payload.Body,
            };
        }

        private InputEventPayload? AcceptInputPayload(InputEventPayload payload) {
            if (payload.InteractionScopeId is { } scopeId) {
                if (activeInteraction is not { } active || active.Scope.Id != scopeId) {
                    return null;
                }
            }

            nextInputSequence = Math.Max(nextInputSequence, payload.InputSequence);
            return payload;
        }

        private LifecyclePayload? AcceptLifecyclePayload(LifecyclePayload payload) {
            if (payload.Phase != LifecyclePhase.Attached
                || payload.InteractionScopeId is not { } scopeId
                || activeInteraction is not { } active
                || active.Scope.Id != scopeId) {
                return null;
            }

            nextLifecycleSequence = Math.Max(nextLifecycleSequence, payload.LifecycleSequence);
            return payload;
        }

        private CompletionAcceptance? AcceptCompletionPayload(SurfaceCompletionPayload payload) {
            if (payload.InteractionScopeId is not { } scopeId
                || activeInteraction is not { } active
                || active.Scope.Id != scopeId) {
                return null;
            }

            nextCompletionSequence = Math.Max(nextCompletionSequence, payload.CompletionSequence);
            var lifecycle = CreateLifecyclePayload(
                active.Scope,
                LifecyclePhase.Completed);
            activeInteraction = null;
            return new CompletionAcceptance(payload, lifecycle);
        }

        private static LifecyclePayload CreateLifecyclePayload(
            InteractionScope scope,
            LifecyclePhase phase) {
            return new LifecyclePayload {
                InteractionScopeId = scope.Id,
                Phase = phase,
                InteractionKind = scope.Kind,
                IsTransient = scope.IsTransient,
            };
        }

        private static void ValidateInteractionScope(InteractionScope scope, ProjectionScope projectionScope) {
            if (projectionScope.Kind != ProjectionScopeKind.Interaction) {
                throw new InvalidOperationException(
                    GetString($"Surface session interaction publication only accepts interaction documents. Actual scope kind: {projectionScope.Kind}."));
            }

            if (!string.Equals(projectionScope.ScopeId, scope.Id.Value, StringComparison.Ordinal)) {
                throw new InvalidOperationException(GetString("Interaction document scope id does not match the active interaction scope."));
            }

            if (!string.Equals(projectionScope.DocumentKind, scope.Kind, StringComparison.Ordinal)) {
                throw new InvalidOperationException(GetString("Interaction document kind does not match the active interaction kind."));
            }
        }

        private static string? ResolveProjectionStyleSignature(ProjectionSnapshotPayload payload) {
            return payload.Body switch {
                ProjectionFullSnapshotBody full => ProjectionStyleDictionaryOps.BuildSignature(full.Document.State.Styles),
                ProjectionUpdateSnapshotBody update when update.Patch.Styles is not null => ProjectionStyleDictionaryOps.BuildSignature(update.Patch.Styles),
                _ => null,
            };
        }

        private void ThrowIfDisposed() {
            ObjectDisposedException.ThrowIf(disposed, this);
        }

        private readonly record struct CompletionAcceptance(
            SurfaceCompletionPayload Completion,
            LifecyclePayload Lifecycle);

        private readonly record struct PreparedSurfacePublication(
            ISurfacePayload Payload,
            LifecyclePayload? Lifecycle);
    }
}
