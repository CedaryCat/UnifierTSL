using System.Buffers;
using Terraria.Localization;
using Terraria.Net.Sockets;
using TrProtocol.NetPackets;
using UnifierTSL.Performance;

namespace UnifierTSL.Network
{
    public abstract class SocketSender(int clientId = -1) : PacketSender
    {
        public ulong ReceivedBytesCount { get; internal set; }
        public ulong SentBytesCount { get; internal set; }
        public uint ReceivedPacketCount { get; internal set; }
        public uint SentPacketCount { get; internal set; }
        public virtual void ResetDataForNewClient() {
            ReceivedBytesCount = 0;
            SentBytesCount = 0;
            ReceivedPacketCount = 0;
            SentPacketCount = 0;
        }
        public abstract ISocket Socket { get; }
        public virtual void SentData() { }

        internal void CountSentBytes(uint size) {
            SentBytesCount += size;
            SentPacketCount += 1;
            if (clientId < Terraria.Main.maxPlayers && clientId >= 0 && UnifiedServerCoordinator.GetClientCurrentlyServer(clientId) is { } server) {
                var perf = server.Performance;
                perf.CurrentFrameData.SentBytesCount += size;
                perf.CurrentFrameData.SentPacketCount += 1;
            }
            PerformanceData.Network.SentBytes(size);
            PerformanceData.Network.SentPacket();
            SentData();
        }

        public sealed override void SendData(byte[] data, int index, int size) {
            ISocket sk = Socket;
            if (!(sk?.IsConnected() ?? false)) {
                return;
            }
            sk.AsyncSend(data, index, size, _ => { }, null);
            CountSentBytes((uint)size);
        }

        public sealed override void SendData(byte[] data, int index, int size, SocketSendCallback callback, object? state = null) {
            ISocket sk = Socket;
            if (!(sk?.IsConnected() ?? false)) {
                return;
            }
            sk.AsyncSend(data, index, size, callback, state);
            CountSentBytes((uint)size);
        }

        protected sealed override byte[] AllocateBuffer(int size) {
            return ArrayPool<byte>.Shared.Rent(size);
        }

        protected sealed override void SendDataAndFreeBuffer(byte[] buffer, int index, int size) {
            ISocket sk = Socket;
            if (!(sk?.IsConnected() ?? false)) {
                ArrayPool<byte>.Shared.Return(buffer);
                return;
            }
            try {
                sk.AsyncSendNoCopy(buffer, index, size, static state => {
                    ArrayPool<byte>.Shared.Return((byte[])state);
                }, buffer);
            }
            catch {
                ArrayPool<byte>.Shared.Return(buffer);
            }
            CountSentBytes((uint)size);
        }

        private record FreeBufferState(byte[] Buffer, SocketSendCallback Callback, object? State);
        protected sealed override void SendDataAndFreeBuffer(byte[] buffer, int index, int size, SocketSendCallback callback, object? state = null) {
            ISocket sk = Socket;
            if (!(sk?.IsConnected() ?? false)) {
                ArrayPool<byte>.Shared.Return(buffer);
                return;
            }
            try {
                sk.AsyncSendNoCopy(buffer, index, size, static boxedState => {
                    FreeBufferState state = (FreeBufferState)boxedState;
                    ArrayPool<byte>.Shared.Return(state.Buffer);
                    state.Callback(state.State);
                }, new FreeBufferState(buffer, callback, state));
            }
            catch {
                ArrayPool<byte>.Shared.Return(buffer);
            }
            CountSentBytes((uint)size);
        }
        public virtual void Kick(NetworkText reason, bool bg = false) {
            ISocket sk = Socket;
            if (sk is null) {
                return;
            }
            SendDynamicPacket(new Kick(reason));
            if (bg) {
                Console.WriteLine(Language.GetTextValue("CLI.ClientWasBooted", sk.GetRemoteAddress().ToString(), reason));
            }
        }
    }
}
