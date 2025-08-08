using System.Buffers;
using System.Text;

namespace UnifierTSL.ConsoleClient.Protocol
{
    public interface IPacket
    {
        public const int PacketHeaderSize = sizeof(int) + sizeof(byte);
        public const int PacketLenSize = sizeof(int);
        public const int PacketIdSize = sizeof(byte);
        public static TPacket Read<TPacket>(Span<byte> content) where TPacket : struct, IPacket<TPacket> {
            return TPacket.Read(content);
        }
        public static TPacket ReadUnmanaged<TPacket>(Span<byte> content) where TPacket : unmanaged, IUnmanagedPacket<TPacket> {
            return IUnmanagedPacket<TPacket>.ReadUnmanaged(content);
        }
        public static TPacket ReadEmpty<TPacket>(Span<byte> content) where TPacket : unmanaged, IEmptyPacket<TPacket> {
            return IEmptyPacket<TPacket>.ReadEmpty(content);
        }
        public unsafe static void Write<TSelf>(Stream stream, TSelf packet) where TSelf : unmanaged, IPacket<TSelf> {
            var bufferSize = sizeof(TSelf) + PacketHeaderSize;
            byte* bufferPtr = stackalloc byte[bufferSize];
            var buffer = new Span<byte>(bufferPtr, bufferSize);

            *(bufferPtr + PacketLenSize) = (byte)TSelf.ID;
            var len = TSelf.WriteContent(buffer[PacketHeaderSize..], packet);
            len += PacketHeaderSize;
            *((int*)bufferPtr) = len;

            var data = buffer[..len].ToArray();

            stream.Write(buffer[..len]);
        }
        public unsafe static void WriteManaged<TSelf>(Stream stream, TSelf packet) where TSelf : struct, IPacket<TSelf> {
            var bufferSize = packet.GetBufferSize();
            var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
            try {
                fixed (byte* bufferPtr = buffer) {

                    *(bufferPtr + PacketLenSize) = (byte)TSelf.ID;
                    var len = TSelf.WriteContent(new Span<byte>(bufferPtr + PacketHeaderSize, bufferSize - PacketHeaderSize), packet);
                    len += PacketHeaderSize;
                    *((int*)bufferPtr) = len;

                    var data = buffer[..len].ToArray();

                    stream.Write(buffer, 0, len);
                }
            }
            finally {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        public static int GetStringBufferSize(string text) {
            return text.Length * 4 + sizeof(int);
        }
        public static int WriteString(Span<byte> buffer, string text) {
            int index = 0;
            int byteCount = Encoding.UTF8.GetByteCount(text);
            uint value = (uint)byteCount;

            do {
                byte b = (byte)(value & 0x7F);
                value >>= 7;
                if (value > 0) b |= 0x80;
                buffer[index++] = b;
            }
            while (value > 0);

            Encoding.UTF8.GetBytes(text, buffer.Slice(index));
            return index + byteCount;
        }
        public static int ReadString(Span<byte> content, out string text) {
            int length = 0;
            int shift = 0;
            int index = 0;
            byte b;

            do {
                b = content[index++];
                length |= (b & 0x7F) << shift;
                shift += 7;
            } while ((b & 0x80) != 0);

            var stringBytes = content.Slice(index, length);
            text = Encoding.UTF8.GetString(stringBytes);
            return index + length;
        }
    }
    public interface IPacket<TSelf> : IPacket where TSelf : struct, IPacket<TSelf>
    {
        public static abstract int ID { get; }
        public static abstract TSelf Read(Span<byte> content);
        public static abstract int WriteContent(Span<byte> buffer, TSelf packet);
        public abstract int GetBufferSize();
    }
    public unsafe interface IUnmanagedPacket<TSelf> : IPacket<TSelf> where TSelf : unmanaged, IUnmanagedPacket<TSelf>
    {
        int IPacket<TSelf>.GetBufferSize() => sizeof(TSelf) + PacketHeaderSize;
        public static TSelf ReadUnmanaged(Span<byte> content) {
            fixed (byte* p = content) {
                return *(TSelf*)p;
            }
        }
        static int WriteUnmanagedContent(Span<byte> buffer, TSelf packet) {
            fixed (byte* p = buffer) {
                *(TSelf*)p = packet;
                return sizeof(TSelf);
            }
        }
        static TSelf IPacket<TSelf>.Read(Span<byte> content) => ReadUnmanaged(content);
        static int IPacket<TSelf>.WriteContent(Span<byte> buffer, TSelf packet) => WriteUnmanagedContent(buffer, packet);
    }
    public interface IEmptyPacket<TSelf> : IUnmanagedPacket<TSelf> where TSelf : unmanaged, IUnmanagedPacket<TSelf>
    {
        int IPacket<TSelf>.GetBufferSize() => PacketHeaderSize;
        public static TSelf ReadEmpty(Span<byte> content) => default;
        static int WriteEmptyContent(Span<byte> buffer, TSelf packet) => 0;
        static TSelf IPacket<TSelf>.Read(Span<byte> content) => ReadEmpty(content);
        static int IPacket<TSelf>.WriteContent(Span<byte> buffer, TSelf packet) => WriteEmptyContent(buffer, packet);
    }
}
