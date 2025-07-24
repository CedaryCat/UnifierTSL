using UnifierTSL.ConsoleClient.Protocol;
using UnifierTSL.ConsoleClient.Protocol.C2S;
using UnifierTSL.ConsoleClient.Protocol.S2C;
using System.Collections.Concurrent;
using System.Text;

namespace UnifierTSL.CLI
{
    partial class ConsoleClientLauncher
    {

        #region Command Implementation

        ConsoleColor cachedBackgroundColor = Console.BackgroundColor;
        ConsoleColor cachedForegroundColor = Console.ForegroundColor;
        Encoding cachedInputEncoding = Console.InputEncoding;
        Encoding cachedOutputEncoding = Console.OutputEncoding;
        int cachedWindowHeight = Console.WindowHeight;
        int cachedWindowLeft = Console.WindowLeft;
        int cachedWindowTop = Console.WindowTop;
        int cachedWindowWidth = Console.WindowWidth;
        string cachedTitle = "";

        public override ConsoleColor BackgroundColor {
            get => cachedBackgroundColor;
            set {
                cachedBackgroundColor = value;
                IPacket.Write(_pipeServer, new SET_BG_COLOR(value));
            }
        }

        public override ConsoleColor ForegroundColor {
            get => cachedForegroundColor;
            set {
                cachedForegroundColor = value;
                IPacket.Write(_pipeServer, new SET_FG_COLOR(value));
            }
        }

        public override Encoding InputEncoding {
            get => cachedInputEncoding;
            set {
                cachedInputEncoding = value;
                IPacket.Write(_pipeServer, new SET_INPUT_ENCODING(value));
            }
        }

        public override Encoding OutputEncoding {
            get => cachedOutputEncoding;
            set {
                cachedOutputEncoding = value;
                IPacket.Write(_pipeServer, new SET_OUTPUT_ENCODING(value));
            }
        }

        public override int WindowWidth {
            get => cachedWindowWidth;
            set {
                cachedWindowWidth = value;
                IPacket.Write(_pipeServer, new SET_WINDOW_SIZE(value, 0));
            }
        }

        public override int WindowHeight {
            get => cachedWindowHeight;
            set {
                cachedWindowHeight = value;
                IPacket.Write(_pipeServer, new SET_WINDOW_SIZE(0, value));
            }
        }

        public override int WindowLeft {
            get => cachedWindowLeft;
            set {
                cachedWindowLeft = value;
                IPacket.Write(_pipeServer, new SET_WINDOW_POS(value, 0));
            }
        }

        public override int WindowTop {
            get => cachedWindowTop;
            set {
                cachedWindowTop = value;
                IPacket.Write(_pipeServer, new SET_WINDOW_POS(0, value));
            }
        }

        public override string Title {
            get => cachedTitle;
            set {
                cachedTitle = value;
                IPacket.WriteManaged(_pipeServer, new SET_TITLE(value));
            }
        }
        public override void Write(string? value) {
            if (value == null) return;
            IPacket.WriteManaged(_pipeServer, new SEND_WRITE(value));
        }

        public override void WriteLine(string? value) {
            if (value == null) return;
            IPacket.WriteManaged(_pipeServer, new SEND_WRITE_LINE(value));
        }

        public override void Clear() {
            IPacket.Write(_pipeServer, new CLEAR());
        }
        #endregion

        #region Read Implementation
        class CurrentWaitingData
        {
            public SEND_READ_FLAG packet;
            public bool confirmedWaiting = false;
        }
        class WaitingData
        {
            public WaitingData(ConsoleClientLauncher client) {
                this.client = client;
                var updateThread = new Thread(Update) {
                    IsBackground = true
                };
                updateThread.Start();
            }
            int timeOutCounter = 0;
            void Update() {
                while (true) {
                    lock (waitingLock) {
                        if (currentWaiting is not null && !currentWaiting.confirmedWaiting) {
                            timeOutCounter += 1;
                            if (timeOutCounter > 20) {
                                if (client._pipeServer.IsConnected) {
                                    timeOutCounter = 0;
                                    IPacket.Write(client._pipeServer, currentWaiting.packet);
                                }
                            }
                        }
                    }
                    Thread.Sleep(50);
                }
            }
            readonly ConsoleClientLauncher client;
            readonly Lock waitingLock = new();
            public long currentSentReadOrder = 0;
            public readonly Queue<ReadFlags> unsentWaitings = [];
            public CurrentWaitingData? currentWaiting = null;
            readonly BlockingCollection<object> inputs = [.. new ConcurrentQueue<object>()];

            public void MoveNext() {
                lock (waitingLock) {
                    currentWaiting = null;
                    if (unsentWaitings.TryDequeue(out var flag)) {
                        EnqueueFlagInner(flag);
                    }
                }
            }
            void EnqueueFlagInner(ReadFlags flag) {
                var packet = new SEND_READ_FLAG(flag, currentSentReadOrder++);
                if (currentWaiting is null) {
                    timeOutCounter = 0;
                    currentWaiting = new CurrentWaitingData { packet = packet };
                    if (client._pipeServer.IsConnected) {
                        IPacket.Write(client._pipeServer, packet);
                    }
                }
                else {
                    unsentWaitings.Enqueue(flag);
                }
            }
            public void EnqueueFlag(ReadFlags flag) {
                lock (waitingLock) {
                    EnqueueFlagInner(flag);
                }
            }
            public object FetchResult() => inputs.Take();
            public void ProcessData(byte id, Span<byte> content) {
                lock (waitingLock) {
                    switch (id) {
                        case CONFIRM_READ_FLAG.id:
                            var flag = IPacket.ReadUnmanaged<CONFIRM_READ_FLAG>(content);
                            if (currentWaiting is null) {
                                throw new Exception("Unexpected waiting state");
                            }
                            if (currentWaiting.packet.Order != flag.Order) {
                                throw new Exception("Unexpected waiting order");
                            }
                            currentWaiting.confirmedWaiting = true;
                            break;
                        case PUSH_READ.id:
                            var read = IPacket.ReadUnmanaged<PUSH_READ>(content);
                            inputs.Add(read.ReadResult);
                            break;
                        case PUSH_READKEY.id:
                            var readkey = IPacket.ReadUnmanaged<PUSH_READKEY>(content);
                            inputs.Add(readkey.KeyInfo);
                            break;
                        case PUSH_READLINE.id:
                            var readline = IPacket.Read<PUSH_READLINE>(content);
                            inputs.Add(readline.Line);
                            break;
                    }
                }
            }
            public void HandleRestart() {
                lock (waitingLock) {
                    if (currentWaiting is not null && currentWaiting.confirmedWaiting) {
                        currentWaiting.confirmedWaiting = false;
                        timeOutCounter = 20;
                    }
                }
            }
        }

        readonly WaitingData waiting;
        readonly Lock readLock = new();
        public override string? ReadLine() {
            lock (readLock) {
                waiting.EnqueueFlag(ReadFlags.ReadLine);
                var value = waiting.FetchResult();
                waiting.MoveNext();
                return (string)value;
            }
        }
        public override ConsoleKeyInfo ReadKey() {
            lock (readLock) {
                waiting.EnqueueFlag(ReadFlags.ReadKey);
                var value = waiting.FetchResult();
                waiting.MoveNext();
                return (ConsoleKeyInfo)value;
            }
        }
        public override ConsoleKeyInfo ReadKey(bool intercept) {
            lock (readLock) {
                if (!intercept) {
                    waiting.EnqueueFlag(ReadFlags.ReadKey);
                    var value = waiting.FetchResult();
                    waiting.MoveNext();
                    return (ConsoleKeyInfo)value;
                }
                else {
                    waiting.EnqueueFlag(ReadFlags.ReadKeyIntercept);
                    var value = waiting.FetchResult();
                    waiting.MoveNext();
                    return (ConsoleKeyInfo)value;
                }
            }
        }
        public override int Read() {
            lock (readLock) {
                waiting.EnqueueFlag(ReadFlags.Read);
                var value = waiting.FetchResult();
                waiting.MoveNext();
                return (int)value;
            }
        }
        #endregion
    }
}
