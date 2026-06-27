using System.Runtime.CompilerServices;
using TrProtocol;
using TrProtocol.Interfaces;
using TrProtocol.Models;
using UnifierTSL.Events.Handlers;
using UnifierTSL.Servers;

namespace Kaleido.Hosting.SharedProjection
{
    internal static class ProjectionPacketReader
    {
        public static unsafe TPacket ReadPacket<TPacket>(ref readonly ReceiveBytesInfo info, int contentOffset = 1) where TPacket : struct, INetPacket {
            var packet = default(TPacket);
            if (packet is ISideSpecific) {
                var boxed = (object)packet;
                ((ISideSpecific)boxed).IsServerSide = true;
                packet = (TPacket)boxed;
            }
            void* ptr = Unsafe.Add<byte>(info.rawDataBegin, contentOffset);
            packet.ReadContent(ref ptr, info.rawDataEnd);
            return packet;
        }

        public static unsafe void ForwardOriginal(ProcessPacketEvent packet, ref readonly ReceiveBytesInfo info) {
            var msgBuffer = ServerRuntime.MessageBuffers[packet.ReceiveFrom.ID];
            int begin;
            int length;
            fixed (byte* ptr = msgBuffer.readBuffer) {
                begin = (int)((byte*)packet.rawDataBegin - ptr);
                length = (int)((byte*)packet.rawDataEnd - (byte*)packet.rawDataBegin);
            }
            msgBuffer.GetData(packet.LocalReceiver.Server, begin, length, out _);
        }
    }
}
