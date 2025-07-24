using Terraria.Localization;
using Terraria.Net.Sockets;
using TrProtocol.NetPackets;
using System.Buffers;

namespace UnifierTSL.Network
{
    public abstract class SocketSender() : PacketSender
    {
        public abstract ISocket Socket { get; }
        public override void SendData(byte[] data, int index, int size) {
            var sk = Socket;
            if (!sk.IsConnected()) {
                return;
            }
            sk.AsyncSend(data, index, size, _ => { }, null);
        }

        public override void SendData(byte[] data, int index, int size, SocketSendCallback callback, object? state = null) {
            var sk = Socket;
            if (!sk.IsConnected()) {
                return;
            }
            sk.AsyncSend(data, index, size, callback, state);
        }

        protected sealed override byte[] AllocateBuffer(int size) {
            return ArrayPool<byte>.Shared.Rent(size);
        }

        protected sealed override void SendDataAndFreeBuffer(byte[] buffer, int index, int size) {
            var sk = Socket;
            if (!sk.IsConnected()) {
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
        }
        record FreeBufferState(byte[] Buffer, SocketSendCallback Callback, object? State);
        protected sealed override void SendDataAndFreeBuffer(byte[] buffer, int index, int size, SocketSendCallback callback, object? state = null) {
            var sk = Socket;
            if (!sk.IsConnected()) {
                ArrayPool<byte>.Shared.Return(buffer);
                return;
            }
            try {
                sk.AsyncSendNoCopy(buffer, index, size, static boxedState => {
                    var state = (FreeBufferState)boxedState;
                    ArrayPool<byte>.Shared.Return(state.Buffer);
                    state.Callback(state.State);
                }, new FreeBufferState(buffer, callback, state));
            }
            catch {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        public virtual void Kick(NetworkText reason, bool bg = false) {
            SendDynamicPacket(new Kick(reason));
            var sk = Socket;
            if (bg) {
                Console.WriteLine(Language.GetTextValue("CLI.ClientWasBooted", sk.GetRemoteAddress().ToString(), reason));
            }
        }
    }
}
