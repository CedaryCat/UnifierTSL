using UnifierTSL.ConsoleClient.Protocol;
using System.IO.Pipes;

namespace UnifierTSL.ConsoleClient
{
    public sealed class ConsoleClient : IDisposable
    {
        private readonly NamedPipeClientStream _pipeClient;
        private readonly PacketFrameBuffer packetFrameBuffer = new();
        private int disposed;

        public ConsoleClient(string pipeName) {
            _pipeClient = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            _pipeClient.Connect();

            // start a thread to listen for commands
            var listenThread = new Thread(ListenForCommands) {
                IsBackground = true
            };
            listenThread.Start();
        }

        void ListenForCommands() {
            try {
                while (_pipeClient.IsConnected) {
                    packetFrameBuffer.Read(_pipeClient, (packetId, buffer, offset, length) =>
                        ConsoleClientLogic.ProcessData(this, packetId, new Span<byte>(buffer, offset, length)));
                }
            }
            catch (Exception ex) {
                Console.WriteLine($"Error in client: {ex}");
            }
            finally {
                Environment.Exit(0);
            }
        }

        public void Send<TPacket>(TPacket packet) where TPacket : unmanaged, IPacket<TPacket> {
            IPacket.Write(_pipeClient, packet);
        }
        public void SendManaged<TPacket>(TPacket packet) where TPacket : struct, IPacket<TPacket> {
            IPacket.WriteManaged(_pipeClient, packet);
        }

        public void Dispose() {
            if (Interlocked.Exchange(ref disposed, 1) != 0) {
                return;
            }

            _pipeClient.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
