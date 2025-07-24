using System.Diagnostics;
using System.Runtime.CompilerServices;
using Terraria.Net.Sockets;
using TrProtocol;
using TrProtocol.Interfaces;
using UnifierTSL.Collections;

namespace UnifierTSL.Network
{
    public unsafe abstract class PacketSender
    {
        static bool enableTrackData;
        public static bool EnableTrackData {
            get => enableTrackData;
            set {
                enableTrackData = value;
            }
        }
        public static readonly DefaultDictionary<string, int>[] TrackedDatas;
        static PacketSender() {
            TrackedDatas = new DefaultDictionary<string, int>[(int)MessageID.Count];
            for (int i = 0; i < TrackedDatas.Length; i++) {
                TrackedDatas[i] = [];
            }
        }
        public abstract void SendData(byte[] data, int index, int size);
        public abstract void SendData(byte[] data, int index, int size, SocketSendCallback callback, object? state = null);
        protected abstract void SendDataAndFreeBuffer(byte[] buffer, int index, int size);
        protected abstract void SendDataAndFreeBuffer(byte[] buffer, int index, int size, SocketSendCallback callback, object? state = null);
        protected abstract byte[] AllocateBuffer(int size);
        private void SendPacketInner<TNetPacket>(scoped in TNetPacket packet, int bufferSize, SocketSendCallback? callback, object? state) where TNetPacket : struct, INetPacket {
            if (EnableTrackData) {
                var st = new StackTrace();
                var m = st.GetFrame(1)?.GetMethod()!;
                var f = $"{m.DeclaringType?.FullName ?? "[UnknowType]"}.{m.Name}";
                TrackedDatas[(int)packet.Type][f]++;
            }
            var buffer = AllocateBuffer(bufferSize);
            WriteData(buffer, in packet, out var size);
            if (callback is null) {
                SendDataAndFreeBuffer(buffer, 0, size);
            }
            else {
                SendDataAndFreeBuffer(buffer, 0, size, callback, state);
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected unsafe void WriteData<TNetPacket>(byte[] buffer, scoped in TNetPacket packet, out int size) where TNetPacket : struct, INetPacket {
            fixed (void* ptr = buffer) {
                var ptr_current = Unsafe.Add<short>(ptr, 1);
                packet.WriteContent(ref ptr_current);
                var size_short = (short)((long)ptr_current - (long)ptr);
                Unsafe.Write(ptr, size_short);
                size = size_short;
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SendFixedPacket<TNetPacket>(scoped in TNetPacket packet) where TNetPacket : unmanaged, INonSideSpecific, INetPacket
            => SendPacketInner(in packet, sizeof(TNetPacket) + 4, null, null);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SendFixedPacket<TNetPacket>(scoped in TNetPacket packet, SocketSendCallback callback, object? state = null) where TNetPacket : unmanaged, INonSideSpecific, INetPacket 
            => SendPacketInner(in packet, sizeof(TNetPacket) + 4, callback, state);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SendDynamicPacket<TNetPacket>(scoped in TNetPacket packet) where TNetPacket : struct, IManagedPacket, INonSideSpecific, INetPacket 
            => SendPacketInner(in packet, packet.Type == MessageID.TileSection ? 1024 * 16 : 1024, null, null);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SendDynamicPacket<TNetPacket>(scoped in TNetPacket packet, SocketSendCallback callback, object? state = null) where TNetPacket : struct, IManagedPacket, INonSideSpecific, INetPacket 
            => SendPacketInner(in packet, packet.Type == MessageID.TileSection ? 1024 * 16 : 1024, callback, state);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SendFixedPacket_S<TNetPacket>(scoped in TNetPacket packet) where TNetPacket : unmanaged, ISideSpecific, INetPacket {
            ref var p = ref Unsafe.AsRef(in packet);
            p.IsServerSide = true;
            SendPacketInner(in packet, sizeof(TNetPacket) + 4, null, null);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SendFixedPacket_S<TNetPacket>(scoped in TNetPacket packet, SocketSendCallback callback, object? state = null) where TNetPacket : unmanaged, ISideSpecific, INetPacket {
            ref var p = ref Unsafe.AsRef(in packet);
            p.IsServerSide = true;
            SendPacketInner(in packet, sizeof(TNetPacket) + 4, callback, state);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SendDynamicPacket_S<TNetPacket>(scoped in TNetPacket packet) where TNetPacket : struct, IManagedPacket, ISideSpecific, INetPacket {
            ref var p = ref Unsafe.AsRef(in packet);
            p.IsServerSide = true;
            SendPacketInner(in packet, packet.Type == MessageID.TileSection ? 1024 * 16 : 1024, null, null);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SendDynamicPacket_S<TNetPacket>(scoped in TNetPacket packet, SocketSendCallback callback, object? state = null) where TNetPacket : struct, IManagedPacket, ISideSpecific, INetPacket {
            ref var p = ref Unsafe.AsRef(in packet);
            p.IsServerSide = true;
            SendPacketInner(in packet, packet.Type == MessageID.TileSection ? 1024 * 16 : 1024, callback, state);
        }
    }
}
