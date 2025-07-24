using System.Runtime.InteropServices;

namespace UnifierTSL.ConsoleClient.Protocol.C2S
{
    [StructLayout(LayoutKind.Sequential)]
    public struct SEND_WRITE(string text) : IPacket<SEND_WRITE>
    {
        public const int id = 0x02;
        public static int ID => id;
        public string Text = text;

        public static SEND_WRITE Read(Span<byte> content) {
            IPacket.ReadString(content, out var str);
            return new(str);
        }
        public static int WriteContent(Span<byte> buffer, SEND_WRITE packet) {
            return IPacket.WriteString(buffer, packet.Text);
        }
        public readonly int GetBufferSize() {
            return IPacket.PacketHeaderSize + IPacket.GetStringBufferSize(Text);
        }
        public readonly override string ToString() {
            return nameof(SEND_WRITE) + ':' + Text;
        }
    }
}
