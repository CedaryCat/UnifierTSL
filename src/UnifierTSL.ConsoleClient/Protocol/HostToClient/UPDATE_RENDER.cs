using System.Runtime.InteropServices;

namespace UnifierTSL.ConsoleClient.Protocol.HostToClient
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct UPDATE_RENDER(long order, string renderJson) : IPacket<UPDATE_RENDER>
    {
        public const int id = 0x0E;
        public static int ID => id;

        public long Order = order;
        public string RenderJson = renderJson;

        public static UPDATE_RENDER Read(Span<byte> content) {
            fixed (byte* ptr = content) {
                long order = *(long*)ptr;
                IPacket.ReadString(content[sizeof(long)..], out string renderJson);
                return new(order, renderJson);
            }
        }

        public static int WriteContent(Span<byte> buffer, UPDATE_RENDER packet) {
            fixed (byte* ptr = buffer) {
                *(long*)ptr = packet.Order;
            }

            return sizeof(long) + IPacket.WriteString(buffer[sizeof(long)..], packet.RenderJson);
        }

        public readonly int GetBufferSize() {
            return IPacket.PacketHeaderSize + sizeof(long) + IPacket.GetStringBufferSize(RenderJson);
        }

        public readonly override string ToString() {
            return nameof(UPDATE_RENDER) + $":[{Order}]" + RenderJson;
        }
    }
}
