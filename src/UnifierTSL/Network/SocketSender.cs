using System.Buffers;
using Terraria.Localization;
using Terraria.Net.Sockets;
using TrProtocol.NetPackets;

namespace UnifierTSL.Network
{
    public abstract class SocketSender() : PacketSender
    {
        public abstract ISocket Socket { get; }
        public virtual void SentData() { }
        public sealed override void SendData(byte[] data, int index, int size) {
            ISocket sk = Socket;
            if (!(sk?.IsConnected() ?? false)) {
                return;
            }
            sk.AsyncSend(data, index, size, _ => { }, null);
            SentData();
        }

        public sealed override void SendData(byte[] data, int index, int size, SocketSendCallback callback, object? state = null) {
            ISocket sk = Socket;
            if (!(sk?.IsConnected() ?? false)) {
                return;
            }
            sk.AsyncSend(data, index, size, callback, state);
            SentData();
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
            SentData();
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
            SentData();
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
