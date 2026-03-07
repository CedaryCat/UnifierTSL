using System.Runtime.InteropServices;

namespace UnifierTSL.ConsoleClient.Protocol.HostToClient
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct SET_STATUS_BAR(
        long sequence,
        string text,
        int indicatorFrameIntervalMs,
        string indicatorStylePrefix,
        string indicatorFrames) : IPacket<SET_STATUS_BAR>
    {
        public const int id = 0x0F;
        public static int ID => id;

        public long Sequence = sequence;
        public string Text = text;
        public int IndicatorFrameIntervalMs = indicatorFrameIntervalMs;
        public string IndicatorStylePrefix = indicatorStylePrefix;
        public string IndicatorFrames = indicatorFrames;

        public static SET_STATUS_BAR Read(Span<byte> content)
        {
            fixed (byte* ptr = content) {
                long sequence = *(long*)ptr;
                int offset = sizeof(long);
                offset += IPacket.ReadString(content[offset..], out string text);
                int indicatorFrameIntervalMs = *(int*)(ptr + offset);
                offset += sizeof(int);
                offset += IPacket.ReadString(content[offset..], out string indicatorStylePrefix);
                offset += IPacket.ReadString(content[offset..], out string indicatorFrames);
                return new(
                    sequence,
                    text,
                    indicatorFrameIntervalMs,
                    indicatorStylePrefix,
                    indicatorFrames);
            }
        }

        public static int WriteContent(Span<byte> buffer, SET_STATUS_BAR packet)
        {
            fixed (byte* ptr = buffer) {
                *(long*)ptr = packet.Sequence;
            }

            int offset = sizeof(long);
            offset += IPacket.WriteString(buffer[offset..], packet.Text);
            fixed (byte* ptr = buffer) {
                *(int*)(ptr + offset) = packet.IndicatorFrameIntervalMs;
            }
            offset += sizeof(int);
            offset += IPacket.WriteString(buffer[offset..], packet.IndicatorStylePrefix);
            offset += IPacket.WriteString(buffer[offset..], packet.IndicatorFrames);
            return offset;
        }

        public readonly int GetBufferSize()
        {
            return IPacket.PacketHeaderSize
                + IPacket.GetStringBufferSize(Text)
                + sizeof(int)
                + IPacket.GetStringBufferSize(IndicatorStylePrefix)
                + IPacket.GetStringBufferSize(IndicatorFrames)
                + sizeof(long);
        }

        public readonly override string ToString()
        {
            return nameof(SET_STATUS_BAR)
                + $":[{Sequence}]"
                + $" {Text}"
                + $" interval:{IndicatorFrameIntervalMs}ms"
                + $" frames:{IndicatorFrames}";
        }
    }
}
