namespace UnifierTSL.Contracts.Protocol {
    public sealed class PacketFrameBuffer {
        private readonly byte[] buffer;
        private int bufferedLength;

        public PacketFrameBuffer(int maxPacketSize = 1024 * 1024) {
            buffer = new byte[maxPacketSize];
        }

        public void Read(Stream stream, Action<byte, byte[], int, int> onPacket) {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentNullException.ThrowIfNull(onPacket);

            int count = stream.Read(buffer, bufferedLength, buffer.Length - bufferedLength);
            if (count == 0) {
                return;
            }

            bufferedLength += count;
            int currentReadPosition = 0;
            int restLength = bufferedLength;

            while (restLength >= IPacket.PacketLenSize) {
                int packetLength = BitConverter.ToInt32(buffer, currentReadPosition);
                if (packetLength < IPacket.PacketHeaderSize || packetLength > buffer.Length) {
                    throw new InvalidDataException($"Invalid packet length: {packetLength}");
                }

                if (restLength < packetLength) {
                    break;
                }

                int contentOffset = currentReadPosition + IPacket.PacketHeaderSize;
                int contentLength = packetLength - IPacket.PacketHeaderSize;
                byte packetId = buffer[currentReadPosition + IPacket.PacketLenSize];
                onPacket(packetId, buffer, contentOffset, contentLength);

                currentReadPosition += packetLength;
                restLength -= packetLength;
            }

            if (restLength > 0 && currentReadPosition > 0) {
                Buffer.BlockCopy(buffer, currentReadPosition, buffer, 0, restLength);
            }

            bufferedLength = restLength;
        }

        public void Reset() {
            bufferedLength = 0;
        }
    }
}
