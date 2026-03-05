using System.Runtime.InteropServices;

namespace UnifierTSL.ConsoleClient.Protocol.C2S
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct SET_READLINE_RENDER(long order, string json) : IPacket<SET_READLINE_RENDER>
    {
        public const int id = 0x0E;
        public static int ID => id;

        public long Order = order;
        public string RenderJson = json;

        public static SET_READLINE_RENDER Read(Span<byte> content)
        {
            fixed (byte* ptr = content) {
                long order = *(long*)ptr;
                IPacket.ReadString(content[sizeof(long)..], out string json);
                return new(order, json);
            }
        }

        public static int WriteContent(Span<byte> buffer, SET_READLINE_RENDER packet)
        {
            fixed (byte* ptr = buffer) {
                *(long*)ptr = packet.Order;
            }

            return sizeof(long) + IPacket.WriteString(buffer[sizeof(long)..], packet.RenderJson);
        }

        public readonly int GetBufferSize()
        {
            return IPacket.PacketHeaderSize + sizeof(long) + IPacket.GetStringBufferSize(RenderJson);
        }

        public readonly override string ToString()
        {
            return nameof(SET_READLINE_RENDER) + $":[{Order}]" + RenderJson;
        }
    }
}
