using System.Text.Json;
using UnifierTSL.ConsoleClient.Protocol;
using UnifierTSL.ConsoleClient.Protocol.HostToClient;
using UnifierTSL.ConsoleClient.Protocol.ClientToHost;
using UnifierTSL.ConsoleClient.Shared.ConsolePrompting;
using UnifierTSL.CLI.Prompting;
using UnifierTSL.CLI.Status;

namespace UnifierTSL.CLI.Remote
{
    internal sealed class RemoteConsoleReadCoordinator : IDisposable
    {
        private sealed class ReadLineWaitingState
        {
            public required ConsolePromptSessionRunner SessionRunner;
            public required string RenderJson;
        }

        private sealed class CurrentWaitingData
        {
            public required BEGIN_READ Packet;
            public TaskCompletionSource<object> Completion { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            public bool ConfirmedWaiting;
            public bool Completed;
            public ReadLineWaitingState? ReadLine;
        }

        private readonly PipeRemoteConsoleTransport transport;
        private readonly Func<ConsolePromptCompiler> defaultCompilerFactory;
        private readonly Lock waitingLock = new();
        private readonly SemaphoreSlim readGate = new(1, 1);
        private readonly Timer retryTimer;
        private readonly Timer promptRefreshTimer;

        private bool disposed;
        private long nextReadOrder;
        private CurrentWaitingData? currentWaiting;

        private const int BeginReadRetryIntervalMs = 1000;

        public RemoteConsoleReadCoordinator(
            PipeRemoteConsoleTransport transport,
            Func<ConsolePromptCompiler> defaultCompilerFactory) {
            this.transport = transport;
            this.defaultCompilerFactory = defaultCompilerFactory;

            retryTimer = new Timer(
                static state => ((RemoteConsoleReadCoordinator)state!).OnRetryTick(),
                this,
                Timeout.Infinite,
                Timeout.Infinite);
            promptRefreshTimer = new Timer(
                static state => ((RemoteConsoleReadCoordinator)state!).OnPromptRefreshTick(),
                this,
                Timeout.Infinite,
                Timeout.Infinite);
            transport.PacketReceived += OnPacketReceived;
            transport.Reconnected += OnTransportReconnected;
        }

        public string ReadLine() {
            return (string)ExecuteRead(ReadFlags.ReadLine);
        }

        public string ReadLine(ConsolePromptSpec prompt) {
            ArgumentNullException.ThrowIfNull(prompt);
            return (string)ExecuteRead(ReadFlags.ReadLine, prompt);
        }

        public ConsoleKeyInfo ReadKey(bool intercept) {
            return (ConsoleKeyInfo)ExecuteRead(intercept ? ReadFlags.ReadKeyIntercept : ReadFlags.ReadKey);
        }

        public int Read() {
            return (int)ExecuteRead(ReadFlags.Read);
        }

        public void Dispose() {
            CurrentWaitingData? waitingToCancel;
            lock (waitingLock) {
                if (disposed) {
                    return;
                }

                disposed = true;
                waitingToCancel = currentWaiting;
                currentWaiting = null;
            }

            try {
                retryTimer.Dispose();
            }
            catch {
            }
            try {
                promptRefreshTimer.Dispose();
            }
            catch {
            }

            transport.PacketReceived -= OnPacketReceived;
            transport.Reconnected -= OnTransportReconnected;
            waitingToCancel?.Completion.TrySetException(new ObjectDisposedException(nameof(RemoteConsoleReadCoordinator)));
        }

        private object ExecuteRead(ReadFlags flag, ConsolePromptSpec? promptSpec = null) {
            ObjectDisposedException.ThrowIf(disposed, this);
            readGate.Wait();
            CurrentWaitingData? waiting = null;
            try {
                waiting = CreateWaiting(flag, promptSpec);
                lock (waitingLock) {
                    ThrowIfDisposed();
                    currentWaiting = waiting;
                }

                RefreshTimerSchedule();
                SendWaiting(waiting);
                return waiting.Completion.Task.GetAwaiter().GetResult();
            }
            finally {
                lock (waitingLock) {
                    if (ReferenceEquals(currentWaiting, waiting)) {
                        currentWaiting = null;
                    }
                }

                RefreshTimerSchedule();
                readGate.Release();
            }
        }

        private void OnPacketReceived(byte id, byte[] content) {
            if (disposed) {
                return;
            }

            TaskCompletionSource<object>? completion = null;
            object? completionResult = null;
            UPDATE_RENDER? pendingRenderUpdate = null;
            bool refreshTimerSchedule = false;
            lock (waitingLock) {
                switch (id) {
                    case CONFIRM_BEGIN_READ.id:
                        CONFIRM_BEGIN_READ confirm = IPacket.ReadUnmanaged<CONFIRM_BEGIN_READ>(content);
                        if (IsCurrentWaiting(confirm.Order)) {
                            currentWaiting!.ConfirmedWaiting = true;
                            refreshTimerSchedule = true;
                        }
                        break;

                    case PUSH_READ.id:
                        PUSH_READ read = IPacket.ReadUnmanaged<PUSH_READ>(content);
                        if (TryCompleteCurrentWaiting(read.Order, out completion)) {
                            completionResult = read.ReadResult;
                            refreshTimerSchedule = true;
                        }
                        break;

                    case PUSH_READKEY.id:
                        PUSH_READKEY readKey = IPacket.ReadUnmanaged<PUSH_READKEY>(content);
                        if (TryCompleteCurrentWaiting(readKey.Order, out completion)) {
                            completionResult = readKey.KeyInfo;
                            refreshTimerSchedule = true;
                        }
                        break;

                    case PUSH_READLINE.id:
                        PUSH_READLINE readLine = IPacket.Read<PUSH_READLINE>(content);
                        if (TryCompleteCurrentWaiting(readLine.Order, out completion)) {
                            completionResult = readLine.Line;
                            refreshTimerSchedule = true;
                        }
                        break;

                    case PUSH_READLINE_INPUT.id:
                        PUSH_READLINE_INPUT readLineState = IPacket.Read<PUSH_READLINE_INPUT>(content);
                        pendingRenderUpdate = BuildRenderUpdateLocked(readLineState);
                        break;
                }
            }

            if (completion is not null) {
                completion.TrySetResult(completionResult!);
            }

            // Completion and transport writes stay outside waitingLock. Both can synchronously
            // trigger user code or reconnect/write paths, and holding the lock across them would
            // block retries for the single in-flight read or create reentrancy deadlocks.
            if (refreshTimerSchedule) {
                RefreshTimerSchedule();
            }

            if (pendingRenderUpdate is UPDATE_RENDER renderUpdate) {
                transport.SendManaged(renderUpdate);
            }
        }

        private void OnTransportReconnected() {
            if (disposed) {
                return;
            }

            CurrentWaitingData? resend = null;
            lock (waitingLock) {
                nextReadOrder = 0;
                if (currentWaiting is null || currentWaiting.Completed) {
                    return;
                }

                // Reconnect creates a fresh console client that has no memory of the old transport
                // order, while the server-side caller is still blocked in ExecuteRead. Reissue the
                // same logical read with a fresh order and the latest render snapshot so the prompt
                // resumes from current state instead of failing the read or replaying stale UI.
                currentWaiting.Packet.Order = nextReadOrder++;
                if (currentWaiting.ReadLine is ReadLineWaitingState readLine) {
                    currentWaiting.Packet.InitialRenderJson = readLine.RenderJson;
                }
                currentWaiting.ConfirmedWaiting = false;
                resend = currentWaiting;
            }

            RefreshTimerSchedule();
            SendWaiting(resend);
        }

        private void OnRetryTick() {
            if (disposed) {
                return;
            }

            CurrentWaitingData? resend = null;
            lock (waitingLock) {
                if (currentWaiting is null || currentWaiting.Completed) {
                    return;
                }

                if (!currentWaiting.ConfirmedWaiting && transport.IsConnected) {
                    resend = currentWaiting;
                }
            }

            if (resend is not null) {
                SendWaiting(resend);
            }
        }

        private void OnPromptRefreshTick() {
            if (disposed) {
                return;
            }

            UPDATE_RENDER? refreshUpdate = null;
            lock (waitingLock) {
                refreshUpdate = BuildRuntimeRefreshUpdateLocked();
            }

            if (refreshUpdate is UPDATE_RENDER renderUpdate) {
                transport.SendManaged(renderUpdate);
            }
        }

        private CurrentWaitingData CreateWaiting(ReadFlags flag, ConsolePromptSpec? promptSpec) {
            CurrentWaitingData waiting = new() {
                Packet = new BEGIN_READ(flag, nextReadOrder++, string.Empty),
            };

            if (flag != ReadFlags.ReadLine) {
                return waiting;
            }

            ConsolePromptSessionRunner sessionRunner = new(
                CreatePromptCompiler(promptSpec),
                ConsolePromptSessionRunner.PagedRenderOptions);
            ConsolePromptSessionState initialSession = sessionRunner.Current;
            string initialRenderJson = JsonSerializer.Serialize(initialSession.RenderSnapshot);
            waiting.ReadLine = new ReadLineWaitingState {
                SessionRunner = sessionRunner,
                RenderJson = initialRenderJson,
            };
            waiting.Packet.InitialRenderJson = initialRenderJson;

            return waiting;
        }

        private ConsolePromptCompiler CreatePromptCompiler(ConsolePromptSpec? promptSpec) {
            if (promptSpec is not null) {
                return ConsolePromptRegistry.CreateCompiler(
                    promptSpec,
                    ConsolePromptScenario.PagedInitial,
                    ConsolePromptScenario.PagedReactive);
            }

            try {
                return defaultCompilerFactory();
            }
            catch {
                return ConsolePromptRegistry.CreateCompiler(
                    ConsolePromptSpec.CreatePlain(),
                    ConsolePromptScenario.PagedInitial,
                    ConsolePromptScenario.PagedReactive);
            }
        }

        private void SendWaiting(CurrentWaitingData? waiting) {
            if (waiting is null || waiting.Completed || !transport.IsConnected) {
                return;
            }

            transport.SendManaged(waiting.Packet);
        }

        private bool IsCurrentWaiting(long order) {
            return currentWaiting is not null
                && currentWaiting.Packet.Order == order
                && !currentWaiting.Completed;
        }

        private bool TryCompleteCurrentWaiting(
            long order,
            out TaskCompletionSource<object>? completion) {
            completion = null;
            if (!IsCurrentWaiting(order)) {
                return false;
            }

            currentWaiting!.Completed = true;
            completion = currentWaiting.Completion;
            return true;
        }

        private UPDATE_RENDER? BuildRenderUpdateLocked(PUSH_READLINE_INPUT statePacket) {
            if (currentWaiting?.ReadLine is not ReadLineWaitingState readLine
                || currentWaiting.Packet.Order != statePacket.Order
                || currentWaiting.Completed) {
                return null;
            }

            ConsoleInputState reactiveState = new() {
                Purpose = readLine.SessionRunner.Current.InputState.Purpose,
                InputText = statePacket.InputText,
                CursorIndex = statePacket.CursorIndex,
                CompletionIndex = statePacket.CompletionIndex,
                CompletionCount = statePacket.CompletionCount,
                CandidateWindowOffset = Math.Max(0, statePacket.CandidateWindowOffset),
            };

            ConsolePromptSessionState updatedSession = readLine.SessionRunner.Update(reactiveState);
            return BuildRenderPacket(currentWaiting.Packet.Order, readLine, updatedSession.RenderSnapshot);
        }

        private UPDATE_RENDER? BuildRuntimeRefreshUpdateLocked() {
            if (currentWaiting?.ReadLine is not ReadLineWaitingState readLine
                || !currentWaiting.ConfirmedWaiting
                || currentWaiting.Completed) {
                return null;
            }

            if (!readLine.SessionRunner.TryRefreshRuntimeDependencies(out ConsolePromptSessionState refreshedSession)) {
                return null;
            }

            return BuildRenderPacket(currentWaiting.Packet.Order, readLine, refreshedSession.RenderSnapshot);
        }

        private void RefreshTimerSchedule() {
            bool enableRetry = false;
            bool enablePromptRefresh = false;
            lock (waitingLock) {
                if (!disposed
                    && currentWaiting is CurrentWaitingData waiting
                    && !waiting.Completed) {
                    enableRetry = !waiting.ConfirmedWaiting;
                    enablePromptRefresh = waiting.ConfirmedWaiting && waiting.ReadLine is not null;
                }
            }

            ChangeTimer(
                retryTimer,
                enableRetry ? BeginReadRetryIntervalMs : Timeout.Infinite,
                enableRetry ? BeginReadRetryIntervalMs : Timeout.Infinite);
            ChangeTimer(
                promptRefreshTimer,
                enablePromptRefresh ? ConsoleStatusService.RefreshIntervalMs : Timeout.Infinite,
                enablePromptRefresh ? ConsoleStatusService.RefreshIntervalMs : Timeout.Infinite);
        }

        private static void ChangeTimer(Timer timer, int dueTime, int period) {
            try {
                timer.Change(dueTime, period);
            }
            catch {
            }
        }

        private static UPDATE_RENDER? BuildRenderPacket(
            long order,
            ReadLineWaitingState readLine,
            ConsoleRenderSnapshot renderSnapshot) {
            string renderJson = JsonSerializer.Serialize(renderSnapshot);
            if (string.Equals(readLine.RenderJson, renderJson, StringComparison.Ordinal)) {
                return null;
            }

            readLine.RenderJson = renderJson;
            return new UPDATE_RENDER(order, renderJson);
        }

        private void ThrowIfDisposed() {
            ObjectDisposedException.ThrowIf(disposed, this);
        }
    }
}
