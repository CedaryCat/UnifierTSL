namespace UnifierTSL.Contracts.Protocol.Wire {
    public enum SurfaceWirePayloadKind : byte {
        Bootstrap = 1,
        SurfaceHostOperation = 2,
        SurfaceOperation = 3,
        InputEvent = 4,
        Lifecycle = 5,
        SurfaceCompletion = 6,
        ProjectionSnapshotOperation = 7,
    }

    public readonly record struct SurfaceWireEnvelope(
        SurfaceWirePayloadKind Kind,
        byte[] PayloadContent) : IPacket<SurfaceWireEnvelope> {

        public const int id = 0x20;
        public static int ID => id;

        public static SurfaceWireEnvelope Read(Span<byte> content) {
            if (content.Length == 0) {
                throw new InvalidDataException("Surface-client wire envelope is missing the payload kind.");
            }

            IPacket.ReadBytes(content[1..], out var payloadContent);
            return new SurfaceWireEnvelope((SurfaceWirePayloadKind)content[0], payloadContent);
        }

        public static int WriteContent(Span<byte> buffer, SurfaceWireEnvelope packet) {
            buffer[0] = (byte)packet.Kind;
            return 1 + IPacket.WriteBytes(buffer[1..], packet.PayloadContent);
        }

        public int GetBufferSize() {
            return IPacket.PacketHeaderSize + sizeof(byte) + IPacket.GetBytesBufferSize(PayloadContent);
        }
    }
}
