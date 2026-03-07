namespace UnifierTSL.ConsoleClient.Protocol.HostToClient
{
    public unsafe struct SET_CONSOLE_THEME(string themeJson) : IPacket<SET_CONSOLE_THEME>
    {
        public const int id = 0x10;
        public static int ID => id;

        public string ThemeJson = themeJson;

        public static SET_CONSOLE_THEME Read(Span<byte> content)
        {
            IPacket.ReadString(content, out string themeJson);
            return new(themeJson);
        }

        public static int WriteContent(Span<byte> buffer, SET_CONSOLE_THEME packet)
        {
            return IPacket.WriteString(buffer, packet.ThemeJson);
        }

        public readonly int GetBufferSize()
        {
            return IPacket.PacketHeaderSize
                + IPacket.GetStringBufferSize(ThemeJson);
        }
    }
}
