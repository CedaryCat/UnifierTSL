using System.Buffers;
using System.Runtime.CompilerServices;
using TrProtocol;
using TrProtocol.Interfaces;
using UnifierTSL.Servers;

namespace UnifierTSL.Network
{
    public class ClientPacketReciever(ServerContext server)
    {
        public readonly ServerContext Server = server;
        private unsafe void AsRecieveFromSenderInner<TNetPacket>(LocalClientSender sender, scoped in TNetPacket packet, int bufferSize) where TNetPacket : struct, INetPacket {
            var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);

            try {
                fixed (byte* ptr = buffer) {
                    var ptr_current = Unsafe.Add<short>(ptr, 1);
                    packet.WriteContent(ref ptr_current);
                    var size_short = (short)((long)ptr_current - (long)ptr);
                    Unsafe.Write(ptr, size_short);

                    Server.NetMessage.buffer[sender.ID].GetData(Server, 0, size_short, out _, buffer, new(new MemoryStream(buffer)));
                }
            }
            finally {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void AsRecieveFromSender_DynamicPkt<TNetPacket>(LocalClientSender sender, scoped in TNetPacket packet) where TNetPacket : struct, IManagedPacket, INonSideSpecific, INetPacket {
            AsRecieveFromSenderInner(sender, packet, packet.Type == MessageID.TileSection ? 1024 * 8 : 1024);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void AsRecieveFromSender_FixedPkt<TNetPacket>(LocalClientSender sender, scoped in TNetPacket packet) where TNetPacket : unmanaged, INonSideSpecific, INetPacket {
            AsRecieveFromSenderInner(sender, packet, sizeof(TNetPacket) + 4);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void AsRecieveFromSender_DynamicPkt_S<TNetPacket>(LocalClientSender sender, scoped in TNetPacket packet) where TNetPacket : struct, IManagedPacket, ISideSpecific, INetPacket {
            ref var p = ref Unsafe.AsRef(in packet);
            p.IsServerSide = false;
            AsRecieveFromSenderInner(sender, p, p.Type == MessageID.TileSection ? 1024 * 8 : 1024);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void AsRecieveFromSender_FixedPkt_S<TNetPacket>(LocalClientSender sender, TNetPacket packet) where TNetPacket : unmanaged, ISideSpecific, INetPacket {
            ref var p = ref Unsafe.AsRef(in packet);
            p.IsServerSide = false;
            AsRecieveFromSenderInner(sender, p, sizeof(TNetPacket) + 4);
        }
    }
}
