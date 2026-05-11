using System.Threading.Channels;
using UnifierTSL.Contracts.Projection;
using UnifierTSL.Contracts.Projection.BuiltIn;
using UnifierTSL.Contracts.Protocol.Payloads;
using UnifierTSL.Contracts.Sessions;
using UnifierTSL.SurfaceClient.Transport;
using UnifierTSL.Terminal.Runtime;

namespace UnifierTSL.SurfaceClient.Hosting {
    internal sealed class TerminalSurfaceClientRuntime : IDisposable {
        private sealed class PendingInteractionState {
            public LifecyclePayload? Lifecycle;
            public ProjectionDocument? Document;
            public TerminalSurfaceRuntimeFrame? Frame;
            public bool Enqueued;
        }

        private sealed class ActiveInteractionState {
            public required TerminalSurfaceInteraction Interaction;
            public required LifecyclePayload Lifecycle;
            public ProjectionDocument? Document;
            public required TerminalSurfaceRuntimeFrame Frame;
            public required CancellationTokenSource ExecutionCancellation;
            public bool SuppressOutboundCompletion;
        }

        private readonly Lock stateLock = new();
        private readonly Channel<InteractionScopeId> interactionQueue = Channel.CreateUnbounded<InteractionScopeId>();
        private readonly PipeSurfaceClientTransport transport;
        private readonly TerminalSurfaceRuntimeHost surfaceRuntimeHost;
        private readonly Dictionary<InteractionScopeId, PendingInteractionState> pendingInteractions = [];

        private ActiveInteractionState? activeInteraction;
        private ProjectionDocument? statusDocument;

        private long latestStatusSequence = -1;
        private long nextLifecycleSequence;
        private long nextInputSequence;
        private long nextCompletionSequence;
        private bool started;
        private bool disposed;

        public TerminalSurfaceClientRuntime(PipeSurfaceClientTransport transport) {
            this.transport = transport;
            surfaceRuntimeHost = new TerminalSurfaceRuntimeHost();
            transport.PayloadReceived += OnPayloadReceived;
            transport.Disconnected += OnTransportDisconnected;
            Console.CancelKeyPress += HandleCancelKeyPress;
        }

        public Task RunAsync() {
            lock (stateLock) {
                ThrowIfDisposed();
                if (started) {
                    return transport.Completion;
                }

                started = true;
                _ = Task.Run(ProcessInteractionsAsync);
                transport.Start();
                return transport.Completion;
            }
        }

        public void Dispose() {
            lock (stateLock) {
                if (disposed) {
                    return;
                }

                disposed = true;
                CancelActiveInteractionLocked(suppressOutboundCompletion: true);
            }

            interactionQueue.Writer.TryComplete();
            Console.CancelKeyPress -= HandleCancelKeyPress;
            transport.PayloadReceived -= OnPayloadReceived;
            transport.Disconnected -= OnTransportDisconnected;
            surfaceRuntimeHost.Dispose();
        }

        private async Task ProcessInteractionsAsync() {
            await foreach (var scopeId in interactionQueue.Reader.ReadAllAsync()) {
                if (!TryActivateInteraction(scopeId, out var interaction)) {
                    continue;
                }

                try {
                    var completion = surfaceRuntimeHost.ExecuteInteraction(
                        interaction.Interaction,
                        interaction.Frame,
                        bufferedState => TrySendPayload(CreateBufferedInputPayload(interaction.Interaction.Scope, bufferedState)),
                        bufferedState => TrySendPayload(CreateSubmitInputPayload(interaction.Interaction.Scope, bufferedState)),
                        keyInfo => TrySendPayload(CreateKeyInputPayload(interaction.Interaction.Scope, keyInfo)),
                        delta => TrySendPayload(CreateActivitySelectPayload(delta)),
                        interaction.ExecutionCancellation.Token);
                    if (!interaction.SuppressOutboundCompletion) {
                        TrySendPayload(CreateCompletionPayload(interaction.Interaction.Scope, completion));
                    }
                }
                finally {
                    interaction.ExecutionCancellation.Dispose();
                    lock (stateLock) {
                        if (activeInteraction?.Interaction.Scope.Id == interaction.Interaction.Scope.Id) {
                            activeInteraction = null;
                        }
                    }
                }
            }
        }

        private void OnPayloadReceived(ISurfacePayload payload) {
            if (disposed) {
                return;
            }

            switch (payload) {
                case BootstrapPayload bootstrapPayload:
                    ApplyBootstrap(bootstrapPayload);
                    break;

                case SurfaceHostOperationPayload hostOperation:
                    ApplySurfaceHostOperation(hostOperation);
                    break;

                case SurfaceOperationPayload surfaceOperation:
                    ApplySurfaceOperation(surfaceOperation);
                    break;

                case ProjectionSnapshotOperationPayload projectionSnapshot:
                    ApplyProjectionSnapshot(projectionSnapshot.Snapshot);
                    break;
            }
        }

        private void ApplyBootstrap(BootstrapPayload payload) {
            lock (stateLock) {
                CancelActiveInteractionLocked(suppressOutboundCompletion: true);
                pendingInteractions.Clear();
                latestStatusSequence = -1;
                statusDocument = null;
                surfaceRuntimeHost.ApplyBootstrap(payload.Bootstrap);
            }
        }

        private void ApplySurfaceHostOperation(SurfaceHostOperationPayload payload) {
            lock (stateLock) {
                if (disposed) {
                    return;
                }
            }

            surfaceRuntimeHost.ApplySurfaceHostOperation(payload.Operation);
        }

        private void ApplySurfaceOperation(SurfaceOperationPayload payload) {
            if (SurfaceOperations.TryGetStream(payload.Operation, out var stream)) {
                surfaceRuntimeHost.ApplyStream(stream);
                return;
            }

            if (SurfaceOperations.TryGetLifecycle(payload.Operation, out var lifecycle)) {
                ApplyLifecycle(lifecycle);
            }
        }

        private void ApplyLifecycle(LifecyclePayload payload) {
            if (payload.InteractionScopeId is not { } scopeId) {
                return;
            }

            bool attachAndQueue = false;
            lock (stateLock) {
                if (activeInteraction is { } active && active.Interaction.Scope.Id == scopeId) {
                    active.Lifecycle = payload;
                    if (!IsExecutableLifecyclePhase(payload.Phase)) {
                        CancelActiveInteractionLocked(suppressOutboundCompletion: true);
                    }
                    return;
                }

                if (!IsExecutableLifecyclePhase(payload.Phase)) {
                    pendingInteractions.Remove(scopeId);
                    return;
                }

                var interaction = GetOrCreatePendingInteractionLocked(scopeId);
                interaction.Lifecycle = payload;
                attachAndQueue = TryQueueInteractionLocked(interaction);
            }

            if (attachAndQueue) {
                TrySendPayload(CreateAttachedLifecyclePayload(payload));
                interactionQueue.Writer.TryWrite(scopeId);
            }
        }

        private void ApplyProjectionSnapshot(ProjectionSnapshotPayload snapshot) {
            var scope = ProjectionDocumentOps.GetScope(snapshot);
            if (SessionStatusProjectionSchema.IsSessionStatusScope(scope)) {
                TerminalSurfaceRuntimeFrame statusFrame;
                lock (stateLock) {
                    var sequence = snapshot.Sequences.DocumentSequence;
                    if (sequence <= latestStatusSequence) {
                        return;
                    }

                    latestStatusSequence = sequence;
                    var result = surfaceRuntimeHost.CreateStatusFrame(snapshot, statusDocument);
                    statusDocument = result.Document;
                    statusFrame = result.Frame;
                }

                surfaceRuntimeHost.ApplyStatusSnapshot(statusFrame);
                return;
            }

            if (scope.Kind != ProjectionScopeKind.Interaction
                || string.IsNullOrWhiteSpace(scope.ScopeId)) {
                return;
            }

            var scopeId = new InteractionScopeId(scope.ScopeId);
            TerminalSurfaceInteraction? activeInteractionRuntime = null;
            TerminalSurfaceRuntimeFrame? activeFrame = null;
            bool applyToActiveDriver = false;
            bool attachAndQueue = false;
            lock (stateLock) {
                if (activeInteraction is { } active && active.Interaction.Scope.Id == scopeId) {
                    var result = surfaceRuntimeHost.CreateInteractionFrame(snapshot, active.Document);
                    activeInteractionRuntime = active.Interaction;
                    active.Document = result.Document;
                    active.Frame = result.Frame;
                    activeFrame = result.Frame;
                    applyToActiveDriver = true;
                }
                else {
                    var interaction = GetOrCreatePendingInteractionLocked(scopeId);
                    var result = surfaceRuntimeHost.CreateInteractionFrame(snapshot, interaction.Document);
                    interaction.Document = result.Document;
                    interaction.Frame = result.Frame;
                    attachAndQueue = TryQueueInteractionLocked(interaction);
                }
            }

            if (applyToActiveDriver) {
                surfaceRuntimeHost.ApplyInteractionFrame(activeInteractionRuntime!, activeFrame!);
                return;
            }

            if (attachAndQueue) {
                var lifecycle = GetPendingLifecycle(scopeId);
                if (lifecycle is not null) {
                    TrySendPayload(CreateAttachedLifecyclePayload(lifecycle));
                    interactionQueue.Writer.TryWrite(scopeId);
                }
            }
        }

        private PendingInteractionState GetOrCreatePendingInteractionLocked(InteractionScopeId scopeId) {
            if (pendingInteractions.TryGetValue(scopeId, out var existing)) {
                return existing;
            }

            var created = new PendingInteractionState();
            pendingInteractions[scopeId] = created;
            return created;
        }

        private bool TryQueueInteractionLocked(PendingInteractionState interaction) {
            if (interaction.Enqueued
                || interaction.Lifecycle?.Phase != LifecyclePhase.Active
                || interaction.Frame is null) {
                return false;
            }

            interaction.Enqueued = true;
            return true;
        }

        private LifecyclePayload? GetPendingLifecycle(InteractionScopeId scopeId) {
            lock (stateLock) {
                return pendingInteractions.TryGetValue(scopeId, out var interaction) ? interaction.Lifecycle : null;
            }
        }

        private bool TryActivateInteraction(InteractionScopeId scopeId, out ActiveInteractionState interaction) {
            lock (stateLock) {
                interaction = null!;
                if (disposed
                    || !pendingInteractions.TryGetValue(scopeId, out var pending)
                    || pending.Lifecycle is null
                    || pending.Frame is null) {
                    return false;
                }

                if (!IsExecutableLifecyclePhase(pending.Lifecycle.Phase)) {
                    pendingInteractions.Remove(scopeId);
                    return false;
                }

                pendingInteractions.Remove(scopeId);
                // Defer interaction construction until the runtime-bearing frame exists,
                // otherwise the bootstrap profile can poison pending scopes before attach.
                var runtimeInteraction = surfaceRuntimeHost.CreateInteraction(scopeId, null, pending.Lifecycle, pending.Frame);
                interaction = new ActiveInteractionState {
                    Interaction = runtimeInteraction,
                    Lifecycle = pending.Lifecycle,
                    Document = pending.Document,
                    Frame = pending.Frame,
                    ExecutionCancellation = new CancellationTokenSource(),
                };
                activeInteraction = interaction;
            }

            surfaceRuntimeHost.ApplyInteractionFrame(interaction.Interaction, interaction.Frame);
            return true;
        }

        private LifecyclePayload CreateAttachedLifecyclePayload(LifecyclePayload source) {
            return new LifecyclePayload {
                InteractionScopeId = source.InteractionScopeId,
                LifecycleSequence = Interlocked.Increment(ref nextLifecycleSequence),
                Phase = LifecyclePhase.Attached,
                InteractionKind = source.InteractionKind,
                IsTransient = source.IsTransient,
            };
        }

        private InputEventPayload CreateBufferedInputPayload(InteractionScope scope, ClientBufferedEditorState bufferedState) {
            var inputSequence = Interlocked.Increment(ref nextInputSequence);
            return new InputEventPayload {
                InteractionScopeId = scope.Id,
                InputSequence = inputSequence,
                BufferedEditorState = bufferedState,
                Event = new InputEvent {
                    Kind = InputEventKind.EditorStateSync,
                },
            };
        }

        private InputEventPayload CreateSubmitInputPayload(InteractionScope scope, ClientBufferedEditorState bufferedState) {
            var inputSequence = Interlocked.Increment(ref nextInputSequence);
            return new InputEventPayload {
                InteractionScopeId = scope.Id,
                InputSequence = inputSequence,
                BufferedEditorState = bufferedState,
                Event = new InputEvent {
                    Kind = InputEventKind.Submit,
                    Text = bufferedState.BufferText,
                },
            };
        }

        private InputEventPayload CreateKeyInputPayload(InteractionScope scope, ConsoleKeyInfo keyInfo) {
            var inputSequence = Interlocked.Increment(ref nextInputSequence);
            return new InputEventPayload {
                InteractionScopeId = scope.Id,
                InputSequence = inputSequence,
                Event = new InputEvent {
                    Kind = InputEventKind.Key,
                    KeyInfo = SurfaceKeyInfo.FromConsoleKeyInfo(keyInfo),
                },
            };
        }

        private InputEventPayload CreateActivitySelectPayload(int delta) {
            var inputSequence = Interlocked.Increment(ref nextInputSequence);
            return new InputEventPayload {
                InputSequence = inputSequence,
                Event = new InputEvent {
                    Kind = InputEventKind.Select,
                    Delta = delta,
                    Command = InteractionCommandIds.ActivitySelectRelative,
                },
            };
        }

        private InputEventPayload CreateActivityCancelPayload() {
            var inputSequence = Interlocked.Increment(ref nextInputSequence);
            return new InputEventPayload {
                InputSequence = inputSequence,
                Event = new InputEvent {
                    Kind = InputEventKind.Cancel,
                    Command = InteractionCommandIds.ActivityCancelSelected,
                },
            };
        }

        private SurfaceCompletionPayload CreateCompletionPayload(InteractionScope scope, SurfaceCompletion completion) {
            return new SurfaceCompletionPayload {
                InteractionScopeId = scope.Id,
                CompletionSequence = Interlocked.Increment(ref nextCompletionSequence),
                Completion = completion,
            };
        }

        private void HandleCancelKeyPress(object? sender, ConsoleCancelEventArgs args) {
            args.Cancel = true;
            if (disposed) {
                return;
            }

            TrySendPayload(CreateActivityCancelPayload());
        }

        private void OnTransportDisconnected() {
            lock (stateLock) {
                CancelActiveInteractionLocked(suppressOutboundCompletion: true);
            }

            interactionQueue.Writer.TryComplete();
        }

        private void CancelActiveInteractionLocked(bool suppressOutboundCompletion) {
            if (activeInteraction is not { } active) {
                return;
            }

            active.SuppressOutboundCompletion |= suppressOutboundCompletion;
            try {
                active.ExecutionCancellation.Cancel();
            }
            catch {
            }
        }

        private static bool IsExecutableLifecyclePhase(LifecyclePhase phase) {
            return phase == LifecyclePhase.Active;
        }

        private bool TrySendPayload(ISurfacePayload payload) {
            lock (stateLock) {
                if (disposed) {
                    return false;
                }
            }

            transport.SendPayload(payload);
            return true;
        }

        private void ThrowIfDisposed() {
            ObjectDisposedException.ThrowIf(disposed, this);
        }
    }
}
