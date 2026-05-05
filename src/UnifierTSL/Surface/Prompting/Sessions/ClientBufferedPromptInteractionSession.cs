using UnifierTSL.Surface.Prompting.Runtime;
using UnifierTSL.Surface.Prompting;
using UnifierTSL.Surface.Status;
using UnifierTSL.Contracts.Projection;
using UnifierTSL.Surface.Prompting.Model;
using UnifierTSL.Contracts.Sessions;

namespace UnifierTSL.Surface.Prompting.Sessions;
    internal readonly record struct ClientBufferedPromptInteractionSessionOptions(
        Func<PromptSurfaceCompiler> CreateCompiler,
        ClientBufferedPromptInteractionDriverOptions PromptDriverOptions,
        string InteractionKind,
        bool SupportsRuntimeRefresh,
        int RuntimeRefreshIntervalMs = StatusProjectionRuntime.RefreshIntervalMs)
    {
        public static ClientBufferedPromptInteractionSessionOptions CreateLocal(PromptSurfaceSpec promptSpec) {

            return new(
                () => PromptRegistry.CreateCompiler(
                    promptSpec,
                    PromptSurfaceScenario.LocalInteractive,
                    PromptSurfaceScenario.LocalInteractive),
                ResolvePromptDriverOptions(promptSpec, ResolveLocalRenderOptions(promptSpec)),
                SurfaceInteractionKinds.AuthoringReadLine,
                SupportsRuntimeRefresh: promptSpec.Purpose == PromptInputPurpose.CommandLine);
        }

        public static ClientBufferedPromptInteractionSessionOptions CreateWindowed(
            PromptSurfaceSpec promptSpec,
            string interactionKind,
            ClientBufferedPromptInteractionDriverOptions? promptDriverOptions = null) {
            ArgumentException.ThrowIfNullOrWhiteSpace(interactionKind);

            return new(
                () => PromptRegistry.CreateCompiler(
                    promptSpec,
                    PromptSurfaceScenario.PagedInitial,
                    PromptSurfaceScenario.PagedReactive),
                promptDriverOptions ?? ResolvePromptDriverOptions(promptSpec, PromptInteractionRunner.PagedRenderOptions),
                interactionKind,
                SupportsRuntimeRefresh: true);
        }

        private static PromptSurfaceProjectionOptions ResolveLocalRenderOptions(PromptSurfaceSpec promptSpec) {
            return promptSpec.Purpose == PromptInputPurpose.CommandLine
                ? PromptInteractionRunner.PagedRenderOptions
                : PromptInteractionRunner.LocalRenderOptions;
        }

        private static ClientBufferedPromptInteractionDriverOptions ResolvePromptDriverOptions(
            PromptSurfaceSpec promptSpec,
            PromptSurfaceProjectionOptions renderOptions) {
            var authoring = promptSpec.BufferedAuthoring;
            return new ClientBufferedPromptInteractionDriverOptions(
                renderOptions,
                authoring);
        }
    }

    internal sealed class ClientBufferedPromptInteractionSession : IDisposable
    {
        private delegate bool PromptSessionUpdate(
            ClientBufferedPromptInteractionDriver promptDriver,
            out ClientBufferedPromptInteractionState state);

        private readonly Lock sync = new();
        private readonly ClientBufferedPromptInteractionDriver promptDriver;
        private readonly string interactionKind;
        private readonly Timer? runtimeRefreshTimer;
        private readonly int runtimeRefreshIntervalMs;

        private bool disposed;

        public ClientBufferedPromptInteractionSession(
            ClientBufferedPromptInteractionSessionOptions options) {
            interactionKind = options.InteractionKind;
            promptDriver = new ClientBufferedPromptInteractionDriver(
                options.CreateCompiler(),
                options.PromptDriverOptions);
            Purpose = promptDriver.Current.InteractionState.Purpose;
            runtimeRefreshIntervalMs = Math.Max(1, options.RuntimeRefreshIntervalMs);
            if (options.SupportsRuntimeRefresh) {
                runtimeRefreshTimer = new Timer(
                    static state => ((ClientBufferedPromptInteractionSession)state!).OnRuntimeRefreshTick(),
                    this,
                    Timeout.Infinite,
                    Timeout.Infinite);
            }
        }

        public PromptInputPurpose Purpose { get; }

        public event Action<ClientBufferedPromptInteractionState>? StateChanged;

        public ProjectionDocument CreateDocument(InteractionScopeId scopeId) {
            lock (sync) {
                ObjectDisposedException.ThrowIf(disposed, this);
                return CreateDocument(scopeId, promptDriver.Current);
            }
        }

        public bool PublishReactiveState(ClientBufferedEditorState reactiveState) {
            return PublishIfChanged((ClientBufferedPromptInteractionDriver driver, out ClientBufferedPromptInteractionState state) =>
                driver.TryUpdate(reactiveState, out state));
        }

        public bool PublishBufferedState(ClientBufferedEditorState bufferedState) {
            return PublishIfChanged((ClientBufferedPromptInteractionDriver driver, out ClientBufferedPromptInteractionState state) =>
                driver.TryUpdate(bufferedState, out state));
        }

        public void SetRuntimeRefreshEnabled(bool enabled) {
            if (runtimeRefreshTimer is null) {
                return;
            }

            lock (sync) {
                if (disposed) {
                    return;
                }
            }

            ChangeTimer(
                runtimeRefreshTimer,
                enabled ? runtimeRefreshIntervalMs : Timeout.Infinite,
                enabled ? runtimeRefreshIntervalMs : Timeout.Infinite);
        }

        public void Dispose() {
            Timer? timerToDispose;
            lock (sync) {
                if (disposed) {
                    return;
                }

                disposed = true;
                timerToDispose = runtimeRefreshTimer;
            }

            if (timerToDispose is null) {
                return;
            }

            try {
                timerToDispose.Dispose();
            }
            catch {
            }
        }

        private void OnRuntimeRefreshTick() {
            try {
                PublishIfChanged((ClientBufferedPromptInteractionDriver driver, out ClientBufferedPromptInteractionState state) =>
                    driver.TryRefreshRuntimeDependencies(out state));
            }
            catch {
            }
        }

        private bool PublishIfChanged(PromptSessionUpdate update) {
            if (!TryCreatePublication(update, out var state)) {
                return false;
            }

            StateChanged?.Invoke(state);
            return true;
        }

        private bool TryCreatePublication(
            PromptSessionUpdate update,
            out ClientBufferedPromptInteractionState state) {
            lock (sync) {
                if (disposed || !update(promptDriver, out var nextState)) {
                    state = default;
                    return false;
                }

                state = nextState;
                return true;
            }
        }

        private ProjectionDocument CreateDocument(
            InteractionScopeId scopeId,
            ClientBufferedPromptInteractionState state) {
            return PromptProjectionDocumentFactory.CreateDocument(
                scopeId,
                interactionKind,
                state.PublicationContent);
        }

        private static void ChangeTimer(Timer timer, int dueTime, int period) {
            try {
                timer.Change(dueTime, period);
            }
            catch {
            }
        }
    }
