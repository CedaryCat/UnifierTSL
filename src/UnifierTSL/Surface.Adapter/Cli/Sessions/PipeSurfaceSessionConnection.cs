using System.IO.Pipes;
using UnifierTSL.Contracts.Protocol;
using UnifierTSL.Contracts.Protocol.Payloads;
using UnifierTSL.Contracts.Protocol.Wire;

namespace UnifierTSL.Surface.Adapter.Cli.Sessions {
    internal sealed class PipeSurfaceSessionConnection : IDisposable {
        private readonly string pipeName;
        private readonly PacketFrameBuffer packetFrameBuffer = new();
        private readonly Lock stateLock = new();
        private readonly Lock writeLock = new();

        private bool disposed;
        private NamedPipeServerStream? pipeServer;

        public bool IsConnected {
            get {
                lock (stateLock) {
                    return pipeServer?.IsConnected == true;
                }
            }
        }

        public PipeSurfaceSessionConnection(string pipeName) {
            ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
            this.pipeName = pipeName;
        }

        public void OpenListener() {
            ThrowIfDisposed();
            NamedPipeServerStream stream = new(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            lock (stateLock) {
                pipeServer = stream;
            }
        }

        public void WaitForConnection() {
            ThrowIfDisposed();
            var stream = GetActivePipeServer();
            stream?.WaitForConnection();
        }

        public void Listen(Action<ISurfacePayload> payloadSink) {
            var stream = GetActivePipeServer();
            if (stream is null) {
                return;
            }

            while (!disposed && stream.IsConnected) {
                packetFrameBuffer.Read(stream, (packetId, buffer, offset, length) => {
                    var content = new byte[length];
                    Buffer.BlockCopy(buffer, offset, content, 0, length);
                    if (!SurfaceWireCodec.TryDecode(packetId, content, out var payload) || payload is null) {
                        throw new InvalidDataException(GetString($"Unsupported surface-client wire packet id: 0x{packetId:X2}."));
                    }

                    payloadSink(payload);
                });
            }
        }

        public void SendPayload(ISurfacePayload payload) {
            var stream = GetActivePipeServer();
            if (stream is null || !stream.IsConnected) {
                return;
            }

            try {
                lock (writeLock) {
                    if (!stream.IsConnected) {
                        return;
                    }

                    IPacket.WriteManaged(stream, SurfaceWireCodec.Encode(payload));
                }
            }
            catch {
            }
        }

        public void Reset() {
            DisposePipeServer();
            packetFrameBuffer.Reset();
        }

        public void Dispose() {
            lock (stateLock) {
                if (disposed) {
                    return;
                }

                disposed = true;
            }

            Reset();
        }

        private NamedPipeServerStream? GetActivePipeServer() {
            lock (stateLock) {
                return pipeServer;
            }
        }

        private void DisposePipeServer() {
            NamedPipeServerStream? stream;
            lock (stateLock) {
                stream = pipeServer;
                pipeServer = null;
            }

            try {
                stream?.Dispose();
            }
            catch {
            }
        }

        private void ThrowIfDisposed() {
            ObjectDisposedException.ThrowIf(disposed, this);
        }
    }
}
