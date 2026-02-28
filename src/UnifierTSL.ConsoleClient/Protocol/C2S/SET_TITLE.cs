using System.Runtime.InteropServices;

namespace UnifierTSL.ConsoleClient.Protocol.C2S
{
    [StructLayout(LayoutKind.Sequential)]
    public struct SET_TITLE(string title) : IPacket<SET_TITLE>
    {
        public const int id = 0x08;
        public static int ID => id;
        public string Title = title;

        public static SET_TITLE Read(Span<byte> content) {
            IPacket.ReadString(content, out var str);
            return new(str);
        }
        public static int WriteContent(Span<byte> buffer, SET_TITLE packet) {
            return IPacket.WriteString(buffer, packet.Title);
        }
        public readonly int GetBufferSize() {
            return IPacket.PacketHeaderSize + IPacket.GetStringBufferSize(Title);
        }

        public readonly override string ToString() {
            return nameof(SET_TITLE) + ':' + Title;
        }
    }
}
