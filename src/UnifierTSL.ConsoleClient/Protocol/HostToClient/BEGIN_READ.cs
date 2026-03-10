namespace UnifierTSL.ConsoleClient.Protocol.HostToClient
{
    public unsafe struct BEGIN_READ(ReadFlags flags, long order, string initialRenderJson) : IPacket<BEGIN_READ>
    {
        public const int id = 0x0B;
        public static int ID => id;
        public ReadFlags Flags = flags;
        public long Order = order;
        public string InitialRenderJson = initialRenderJson;

        public static BEGIN_READ Read(Span<byte> content)
        {
            fixed (byte* ptr = content) {
                ReadFlags flags = *(ReadFlags*)ptr;
                long order = *(long*)(ptr + sizeof(ReadFlags));
                IPacket.ReadString(content[(sizeof(ReadFlags) + sizeof(long))..], out string initialRenderJson);
                return new(flags, order, initialRenderJson);
            }
        }

        public static int WriteContent(Span<byte> buffer, BEGIN_READ packet)
        {
            fixed (byte* ptr = buffer) {
                *(ReadFlags*)ptr = packet.Flags;
                *(long*)(ptr + sizeof(ReadFlags)) = packet.Order;
            }

            return sizeof(ReadFlags)
                + sizeof(long)
                + IPacket.WriteString(buffer[(sizeof(ReadFlags) + sizeof(long))..], packet.InitialRenderJson);
        }

        public readonly int GetBufferSize()
        {
            return IPacket.PacketHeaderSize
                + sizeof(ReadFlags)
                + sizeof(long)
                + IPacket.GetStringBufferSize(InitialRenderJson);
        }

        public readonly override string ToString() {
            return $"{nameof(BEGIN_READ)}:[{Order}]{Flags}";
        }
    }
}
