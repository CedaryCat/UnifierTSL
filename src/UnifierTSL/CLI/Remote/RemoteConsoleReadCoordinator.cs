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
            public long LastRenderRefreshTick;
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

        private bool disposed;
        private int timeoutCounter;
        private long nextReadOrder;
        private CurrentWaitingData? currentWaiting;

        public RemoteConsoleReadCoordinator(
            PipeRemoteConsoleTransport transport,
            Func<ConsolePromptCompiler> defaultCompilerFactory) {
            this.transport = transport;
            this.defaultCompilerFactory = defaultCompilerFactory;

            retryTimer = new Timer(static state => ((RemoteConsoleReadCoordinator)state!).OnRetryTick(), this, 50, 50);
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
                    timeoutCounter = 0;
                    currentWaiting = waiting;
                }

                SendWaiting(waiting);
                return waiting.Completion.Task.GetAwaiter().GetResult();
            }
            finally {
                lock (waitingLock) {
                    if (ReferenceEquals(currentWaiting, waiting)) {
                        currentWaiting = null;
                    }
                }

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
            lock (waitingLock) {
                switch (id) {
                    case CONFIRM_BEGIN_READ.id:
                        CONFIRM_BEGIN_READ confirm = IPacket.ReadUnmanaged<CONFIRM_BEGIN_READ>(content);
                        if (IsCurrentWaiting(confirm.Order)) {
                            currentWaiting!.ConfirmedWaiting = true;
                        }
                        break;

                    case PUSH_READ.id:
                        PUSH_READ read = IPacket.ReadUnmanaged<PUSH_READ>(content);
                        if (TryCompleteCurrentWaiting(read.Order, out completion)) {
                            completionResult = read.ReadResult;
                        }
                        break;

                    case PUSH_READKEY.id:
                        PUSH_READKEY readKey = IPacket.ReadUnmanaged<PUSH_READKEY>(content);
                        if (TryCompleteCurrentWaiting(readKey.Order, out completion)) {
                            completionResult = readKey.KeyInfo;
                        }
                        break;

                    case PUSH_READLINE.id:
                        PUSH_READLINE readLine = IPacket.Read<PUSH_READLINE>(content);
                        if (TryCompleteCurrentWaiting(readLine.Order, out completion)) {
                            completionResult = readLine.Line;
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

                currentWaiting.Packet.Order = nextReadOrder++;
                if (currentWaiting.ReadLine is ReadLineWaitingState readLine) {
                    currentWaiting.Packet.InitialRenderJson = readLine.RenderJson;
                }
                currentWaiting.ConfirmedWaiting = false;
                timeoutCounter = 0;
                resend = currentWaiting;
            }

            SendWaiting(resend);
        }

        private void OnRetryTick() {
            if (disposed) {
                return;
            }

            CurrentWaitingData? resend = null;
            UPDATE_RENDER? refreshUpdate = null;
            long nowTick = Environment.TickCount64;
            lock (waitingLock) {
                if (currentWaiting is null || currentWaiting.Completed) {
                    return;
                }

                if (!currentWaiting.ConfirmedWaiting) {
                    timeoutCounter += 1;
                    if (timeoutCounter > 20 && transport.IsConnected) {
                        timeoutCounter = 0;
                        resend = currentWaiting;
                    }
                }

                refreshUpdate = BuildPeriodicRenderUpdateLocked(nowTick);
            }

            if (resend is not null) {
                SendWaiting(resend);
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
                LastRenderRefreshTick = Environment.TickCount64,
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
            readLine.LastRenderRefreshTick = Environment.TickCount64;
            return BuildRenderPacket(currentWaiting.Packet.Order, readLine, updatedSession.RenderSnapshot);
        }

        private UPDATE_RENDER? BuildPeriodicRenderUpdateLocked(long nowTick) {
            if (currentWaiting?.ReadLine is not ReadLineWaitingState readLine
                || !currentWaiting.ConfirmedWaiting
                || currentWaiting.Completed) {
                return null;
            }

            if ((nowTick - readLine.LastRenderRefreshTick) < ConsoleStatusService.RefreshIntervalMs) {
                return null;
            }

            ConsolePromptSessionState refreshedSession = readLine.SessionRunner.Refresh();
            readLine.LastRenderRefreshTick = nowTick;
            return BuildRenderPacket(currentWaiting.Packet.Order, readLine, refreshedSession.RenderSnapshot);
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
