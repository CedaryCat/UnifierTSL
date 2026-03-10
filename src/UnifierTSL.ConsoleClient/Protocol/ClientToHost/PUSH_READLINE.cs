using System.Runtime.InteropServices;

namespace UnifierTSL.ConsoleClient.Protocol.ClientToHost
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct PUSH_READLINE(string line, long order) : IPacket<PUSH_READLINE>
    {
        public const int id = 0x03;
        public static int ID => id;
        public string Line = line;
        public long Order = order;

        public static PUSH_READLINE Read(Span<byte> content) {
            var offset = IPacket.ReadString(content, out var str);
            fixed (byte* p = content) {
                var order = *(long*)(p + offset);
                return new(str, order);
            }
        }

        public static int WriteContent(Span<byte> buffer, PUSH_READLINE packet) {
            int offset = IPacket.WriteString(buffer, packet.Line);
            fixed (byte* p = buffer) {
                *(long*)(p + offset) = packet.Order;
            }
            return offset + sizeof(long);
        }
        public readonly int GetBufferSize() {
            return IPacket.PacketHeaderSize + IPacket.GetStringBufferSize(Line) + sizeof(long);
        }
        public readonly override string ToString() {
            return nameof(PUSH_READLINE) + $":[{Order}]" + Line;
        }
    }
}
