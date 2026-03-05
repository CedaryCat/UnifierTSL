using System.Runtime.InteropServices;

namespace UnifierTSL.ConsoleClient.Protocol.C2S
{
    [StructLayout(LayoutKind.Sequential)]
    public struct SEND_WRITE_ANSI(string text) : IPacket<SEND_WRITE_ANSI>
    {
        public const int id = 0x0C;
        public static int ID => id;

        public string Text = text;

        public static SEND_WRITE_ANSI Read(Span<byte> content)
        {
            IPacket.ReadString(content, out string str);
            return new(str);
        }

        public static int WriteContent(Span<byte> buffer, SEND_WRITE_ANSI packet)
        {
            return IPacket.WriteString(buffer, packet.Text);
        }

        public readonly int GetBufferSize()
        {
            return IPacket.PacketHeaderSize + IPacket.GetStringBufferSize(Text);
        }

        public readonly override string ToString()
        {
            return nameof(SEND_WRITE_ANSI) + ':' + Text;
        }
    }
}
