using System.Collections.Concurrent;
using System.Text.Json;
using UnifierTSL.ConsoleClient.Protocol;
using UnifierTSL.ConsoleClient.Protocol.C2S;
using UnifierTSL.ConsoleClient.Protocol.S2C;
using UnifierTSL.ConsoleClient.Shell;
using UnifierTSL.CLI.Transports;

namespace UnifierTSL.CLI.Sessions
{
    internal sealed class ReadSessionBroker : IReadSessionBroker
    {
        private sealed class CurrentWaitingData
        {
            public SEND_READ_FLAG Packet;
            public SET_READLINE_RENDER RenderPacket;
            public bool HasRenderPacket;
            public bool ConfirmedWaiting;
            public bool Completed;
            public IReadLineSemanticProvider? SemanticProvider;
            public ConsoleInputPurpose Purpose;
            public string LastRenderJson = string.Empty;
            public string LastInputText = string.Empty;
            public int LastCursorIndex;
            public int LastCompletionIndex;
            public int LastCompletionCount;
            public int LastCandidateWindowOffset;
        }

        private readonly IConsoleSessionTransport transport;
        private readonly Func<IReadLineSemanticProvider> semanticProviderFactory;

        private readonly Lock waitingLock = new();
        private readonly SemaphoreSlim readGate = new(1, 1);
        private readonly Timer retryTimer;
        private readonly Queue<ReadFlags> unsentWaitings = [];
        private readonly BlockingCollection<object> inputs = [.. new ConcurrentQueue<object>()];

        private bool disposed;
        private int timeoutCounter;
        private long currentSentReadOrder;
        private CurrentWaitingData? currentWaiting;

        public ReadSessionBroker(
            IConsoleSessionTransport transport,
            Func<IReadLineSemanticProvider> semanticProviderFactory)
        {
            this.transport = transport;
            this.semanticProviderFactory = semanticProviderFactory;

            retryTimer = new Timer(static state => ((ReadSessionBroker)state!).OnRetryTick(), this, 50, 50);
            transport.PacketReceived += OnPacketReceived;
            transport.Reconnected += OnTransportReconnected;
        }

        public string ReadLine()
        {
            object value = ExecuteRead(ReadFlags.ReadLine);
            return (string)value;
        }

        public ConsoleKeyInfo ReadKey(bool intercept)
        {
            object value = ExecuteRead(intercept ? ReadFlags.ReadKeyIntercept : ReadFlags.ReadKey);
            return (ConsoleKeyInfo)value;
        }

        public int Read()
        {
            object value = ExecuteRead(ReadFlags.Read);
            return (int)value;
        }

        public void Dispose()
        {
            if (disposed) {
                return;
            }

            disposed = true;
            retryTimer.Dispose();
            transport.PacketReceived -= OnPacketReceived;
            transport.Reconnected -= OnTransportReconnected;
            inputs.CompleteAdding();
        }

        private object ExecuteRead(ReadFlags flag)
        {
            readGate.Wait();
            try {
                EnqueueFlag(flag);
                object value = inputs.Take();
                MoveNext();
                return value;
            }
            finally {
                readGate.Release();
            }
        }

        private void OnPacketReceived(byte id, byte[] content)
        {
            if (disposed) {
                return;
            }

            SET_READLINE_RENDER? pendingRenderUpdate = null;
            lock (waitingLock) {
                switch (id) {
                    case CONFIRM_READ_FLAG.id:
                        CONFIRM_READ_FLAG flag = IPacket.ReadUnmanaged<CONFIRM_READ_FLAG>(content);
                        if (currentWaiting is null || currentWaiting.Packet.Order != flag.Order || currentWaiting.Completed) {
                            break;
                        }
                        currentWaiting.ConfirmedWaiting = true;
                        break;
                    case PUSH_READ.id:
                        PUSH_READ read = IPacket.ReadUnmanaged<PUSH_READ>(content);
                        if (currentWaiting is null || currentWaiting.Packet.Order != read.Order || currentWaiting.Completed) {
                            break;
                        }
                        currentWaiting.Completed = true;
                        inputs.Add(read.ReadResult);
                        break;
                    case PUSH_READKEY.id:
                        PUSH_READKEY readkey = IPacket.ReadUnmanaged<PUSH_READKEY>(content);
                        if (currentWaiting is null || currentWaiting.Packet.Order != readkey.Order || currentWaiting.Completed) {
                            break;
                        }
                        currentWaiting.Completed = true;
                        inputs.Add(readkey.KeyInfo);
                        break;
                    case PUSH_READLINE.id:
                        PUSH_READLINE readline = IPacket.Read<PUSH_READLINE>(content);
                        if (currentWaiting is null || currentWaiting.Packet.Order != readline.Order || currentWaiting.Completed) {
                            break;
                        }
                        currentWaiting.Completed = true;
                        inputs.Add(readline.Line);
                        break;
                    case PUSH_READLINE_INPUT.id:
                        PUSH_READLINE_INPUT readLineState = IPacket.Read<PUSH_READLINE_INPUT>(content);
                        pendingRenderUpdate = BuildRenderUpdateLocked(readLineState);
                        break;
                }
            }

            if (pendingRenderUpdate is SET_READLINE_RENDER update) {
                transport.SendManaged(update);
            }
        }

        private void OnTransportReconnected()
        {
            if (disposed) {
                return;
            }

            CurrentWaitingData? resend;
            lock (waitingLock) {
                currentSentReadOrder = 0;

                if (currentWaiting is null || currentWaiting.Completed) {
                    return;
                }

                long reconnectOrder = currentSentReadOrder++;
                currentWaiting.Packet.Order = reconnectOrder;
                if (currentWaiting.HasRenderPacket) {
                    currentWaiting.RenderPacket.Order = reconnectOrder;
                }
                currentWaiting.ConfirmedWaiting = false;
                timeoutCounter = 0;
                resend = currentWaiting;
            }

            SendCurrentWaitingPacket(resend);
        }

        private void OnRetryTick()
        {
            if (disposed) {
                return;
            }

            CurrentWaitingData? resend;
            lock (waitingLock) {
                if (currentWaiting is null || currentWaiting.ConfirmedWaiting || currentWaiting.Completed) {
                    return;
                }

                timeoutCounter += 1;
                if (timeoutCounter <= 20 || !transport.IsConnected) {
                    return;
                }

                timeoutCounter = 0;
                resend = currentWaiting;
            }

            SendCurrentWaitingPacket(resend);
        }

        private void EnqueueFlag(ReadFlags flag)
        {
            CurrentWaitingData? toSend;
            lock (waitingLock) {
                toSend = EnqueueFlagInner(flag);
            }

            SendCurrentWaitingPacket(toSend);
        }

        private void MoveNext()
        {
            CurrentWaitingData? toSend = null;
            lock (waitingLock) {
                currentWaiting = null;
                if (unsentWaitings.TryDequeue(out ReadFlags flag)) {
                    toSend = EnqueueFlagInner(flag);
                }
            }

            SendCurrentWaitingPacket(toSend);
        }

        private CurrentWaitingData? EnqueueFlagInner(ReadFlags flag)
        {
            SEND_READ_FLAG packet = new(flag, currentSentReadOrder++);
            CurrentWaitingData waitingData = new() {
                Packet = packet,
            };

            if (flag == ReadFlags.ReadLine) {
                IReadLineSemanticProvider semanticProvider = semanticProviderFactory();
                waitingData.SemanticProvider = semanticProvider;

                ReadLineSemanticSnapshot initialSemantic = semanticProvider.BuildInitial();
                ReadLineReactiveState initialState = new() {
                    Purpose = initialSemantic.Payload.Purpose,
                    InputText = string.Empty,
                    CursorIndex = 0,
                    CompletionIndex = 0,
                    CompletionCount = 0,
                    CandidateWindowOffset = 0,
                };
                ReadLineRenderSnapshot initialRender = ReadLineRenderPaging.BuildSnapshot(initialSemantic, initialState);
                string json = JsonSerializer.Serialize(initialRender);
                waitingData.LastRenderJson = json;
                waitingData.Purpose = initialSemantic.Payload.Purpose;
                waitingData.RenderPacket = new SET_READLINE_RENDER(packet.Order, json);
                waitingData.HasRenderPacket = true;
            }

            if (currentWaiting is null) {
                timeoutCounter = 0;
                currentWaiting = waitingData;
                return waitingData;
            }

            unsentWaitings.Enqueue(flag);
            return null;
        }

        private void SendCurrentWaitingPacket(CurrentWaitingData? waiting)
        {
            if (waiting is null || waiting.Completed || !transport.IsConnected) {
                return;
            }

            if (waiting.HasRenderPacket) {
                transport.SendManaged(waiting.RenderPacket);
            }
            transport.Send(waiting.Packet);
        }

        private SET_READLINE_RENDER? BuildRenderUpdateLocked(PUSH_READLINE_INPUT statePacket)
        {
            if (currentWaiting is null) {
                return null;
            }

            if (currentWaiting.Packet.Flags != ReadFlags.ReadLine) {
                return null;
            }

            if (currentWaiting.Packet.Order != statePacket.Order) {
                return null;
            }

            if (currentWaiting.SemanticProvider is null) {
                return null;
            }

            int candidateWindowOffset = Math.Max(0, statePacket.CandidateWindowOffset);

            if (string.Equals(currentWaiting.LastInputText, statePacket.InputText, StringComparison.Ordinal) &&
                currentWaiting.LastCursorIndex == statePacket.CursorIndex &&
                currentWaiting.LastCompletionIndex == statePacket.CompletionIndex &&
                currentWaiting.LastCompletionCount == statePacket.CompletionCount &&
                currentWaiting.LastCandidateWindowOffset == candidateWindowOffset) {

                return null;
            }

            currentWaiting.LastInputText = statePacket.InputText;
            currentWaiting.LastCursorIndex = statePacket.CursorIndex;
            currentWaiting.LastCompletionIndex = statePacket.CompletionIndex;
            currentWaiting.LastCompletionCount = statePacket.CompletionCount;
            currentWaiting.LastCandidateWindowOffset = candidateWindowOffset;

            ReadLineReactiveState reactiveState = new() {
                Purpose = currentWaiting.Purpose,
                InputText = statePacket.InputText,
                CursorIndex = statePacket.CursorIndex,
                CompletionIndex = statePacket.CompletionIndex,
                CompletionCount = statePacket.CompletionCount,
                CandidateWindowOffset = candidateWindowOffset,
            };

            ReadLineSemanticSnapshot semanticSnapshot = currentWaiting.SemanticProvider.BuildReactive(reactiveState);
            currentWaiting.Purpose = semanticSnapshot.Payload.Purpose;
            reactiveState.Purpose = semanticSnapshot.Payload.Purpose;
            ReadLineRenderSnapshot renderSnapshot = ReadLineRenderPaging.BuildSnapshot(semanticSnapshot, reactiveState);
            string json = JsonSerializer.Serialize(renderSnapshot);
            if (string.Equals(currentWaiting.LastRenderJson, json, StringComparison.Ordinal)) {
                return null;
            }

            currentWaiting.LastRenderJson = json;
            return new SET_READLINE_RENDER(statePacket.Order, json);
        }
    }
}
