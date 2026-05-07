using System.IO.Pipes;
using System.Threading.Channels;
using UnifierTSL.Contracts.Protocol;
using UnifierTSL.Contracts.Protocol.Payloads;
using UnifierTSL.Contracts.Protocol.Wire;

namespace UnifierTSL.SurfaceClient.Transport {
    internal sealed class PipeSurfaceClientTransport : IDisposable {
        private readonly NamedPipeClientStream _pipeClient;
        private readonly PacketFrameBuffer packetFrameBuffer = new();
        private readonly Channel<ISurfacePayload> outboundQueue = Channel.CreateUnbounded<ISurfacePayload>(new UnboundedChannelOptions {
            SingleReader = true,
            SingleWriter = false,
        });
        private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int disposed;
        private int started;

        public event Action<ISurfacePayload>? PayloadReceived;
        public event Action? Disconnected;

        public Task Completion => completion.Task;

        public PipeSurfaceClientTransport(string pipeName) {
            _pipeClient = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            _pipeClient.Connect();
        }

        public void Start() {
            if (Interlocked.Exchange(ref started, 1) != 0) {
                return;
            }

            Thread listenThread = new(ListenForCommands) {
                IsBackground = true,
                Name = "SurfaceClient.Listen"
            };
            _ = Task.Run(ProcessOutboundPayloadsAsync);
            listenThread.Start();
        }

        void ListenForCommands() {
            try {
                while (_pipeClient.IsConnected) {
                    packetFrameBuffer.Read(_pipeClient, (packetId, buffer, offset, length) => {
                        byte[] content = new byte[length];
                        Buffer.BlockCopy(buffer, offset, content, 0, length);
                        if (!SurfaceWireCodec.TryDecode(packetId, content, out var payload) || payload is null) {
                            throw new InvalidDataException($"Unsupported surface-client wire packet id: 0x{packetId:X2}.");
                        }

                        PayloadReceived?.Invoke(payload);
                    });
                }
            }
            catch (Exception ex) {
                if (Volatile.Read(ref disposed) == 0) {
                    Console.WriteLine($"Error in client: {ex}");
                }
            }
            finally {
                outboundQueue.Writer.TryComplete();
                completion.TrySetResult();
                try {
                    Disconnected?.Invoke();
                }
                catch {
                }
            }
        }

        public void SendPayload(ISurfacePayload payload) {
            if (Volatile.Read(ref disposed) != 0) {
                return;
            }

            outboundQueue.Writer.TryWrite(payload);
        }

        private async Task ProcessOutboundPayloadsAsync() {
            try {
                await foreach (var payload in outboundQueue.Reader.ReadAllAsync().ConfigureAwait(false)) {
                    if (Volatile.Read(ref disposed) != 0 || !_pipeClient.IsConnected) {
                        break;
                    }

                    IPacket.WriteManaged(_pipeClient, SurfaceWireCodec.Encode(payload));
                }
            }
            catch {
            }
        }

        public void Dispose() {
            if (Interlocked.Exchange(ref disposed, 1) != 0) {
                return;
            }

            outboundQueue.Writer.TryComplete();
            completion.TrySetResult();
            _pipeClient.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
