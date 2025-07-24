using UnifierTSL.ConsoleClient.Protocol;
using System.IO.Pipes;
using System.Runtime.CompilerServices;

namespace UnifierTSL.ConsoleClient
{
    public class ConsoleClient : IDisposable
    {
        private readonly NamedPipeClientStream _pipeClient;
        private readonly byte[] readBuffer = new byte[1024 * 1024];
        private int bufferWritePosition = 0;
        private bool _disposed = false; // Track whether Dispose has been called

        public ConsoleClient(string pipeName) {
            _pipeClient = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            _pipeClient.Connect();

            // start a thread to listen for commands
            var listenThread = new Thread(ListenForCommands) {
                IsBackground = true
            };
            listenThread.Start();
        }

        unsafe void ListenForCommands() {
            try {
                const int packetLenSize = sizeof(int);
                const int packetIdSize = 1;
                const int packetHeaderSize = packetLenSize + packetIdSize;
                const int maxPacketSize = 1024 * 1024;

                while (_pipeClient.IsConnected) {
                    var count = _pipeClient.Read(readBuffer, bufferWritePosition, readBuffer.Length - bufferWritePosition);
                    if (count == 0) continue;

                    bufferWritePosition += count;
                    int currentReadPosition = 0;
                    int restLen = bufferWritePosition - currentReadPosition;

                    fixed (void* beginPtr = readBuffer) {
                        while (restLen >= packetLenSize) {
                            var packetLen = Unsafe.Read<int>((byte*)beginPtr + currentReadPosition);

                            // length check
                            if (packetLen < packetHeaderSize || packetLen > maxPacketSize) {
                                throw new InvalidDataException($"Invalid packet length: {packetLen}");
                            }

                            if (restLen < packetLen) {
                                break;
                            }

                            ConsoleClientLogic.ProcessData(
                                this,
                                readBuffer[currentReadPosition + packetLenSize],
                                new Span<byte>((byte*)beginPtr + currentReadPosition + packetHeaderSize, packetLen - packetHeaderSize)
                            );

                            currentReadPosition += packetLen;
                            restLen -= packetLen;
                        }
                    }

                    // copy remaining data
                    if (restLen > 0) {
                        for (int i = 0; i < restLen; i++) {
                            readBuffer[i] = readBuffer[currentReadPosition + i];
                        }
                    }
                    bufferWritePosition = restLen;
                }
            }
            catch (Exception ex) {
                Console.WriteLine($"Error in client: {ex}");
            }
            finally {
                Environment.Exit(0);
            }
        }

        public unsafe void Send<TPacket>(TPacket packet) where TPacket : unmanaged, IPacket<TPacket> {
            IPacket.Write(_pipeClient, packet);
        }
        public unsafe void SendManaged<TPacket>(TPacket packet) where TPacket : struct, IPacket<TPacket> {
            IPacket.WriteManaged(_pipeClient, packet);
        }

        public void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this); // Suppress finalization
        }

        protected virtual void Dispose(bool disposing) {
            if (!_disposed) {
                if (disposing) {
                    // Dispose managed resources
                    _pipeClient?.Dispose();
                }

                // Dispose unmanaged resources if any

                _disposed = true;
            }
        }

        ~ConsoleClient() {
            Dispose(false); // Finalizer calls Dispose with false
        }
    }
}
