namespace UnifierTSL.Performance
{
    public partial class ServerPerformance
    {
        public static class Network
        {
            static ulong receivedBytesCount, sentBytesCount;
            static uint receivedPacketsCount, sentPacketsCount;
            public static ulong ReceivedBytesCount => receivedBytesCount;
            public static ulong SentBytesCount => sentBytesCount;
            public static uint ReceivedPacketCount => receivedPacketsCount;
            public static uint SentPacketCount => sentPacketsCount;

            public static void ReceivedBytes(uint amount) {
                Interlocked.Add(ref receivedBytesCount, amount);
            }
            public static void SentBytes(uint amount) {
                Interlocked.Add(ref sentBytesCount, amount);
            }
            public static void ReceivedPackets(uint amount) {
                Interlocked.Add(ref receivedPacketsCount, amount);
            }
            public static void SentPackets(uint amount) {
                Interlocked.Add(ref sentPacketsCount, amount);
            }
            public static void ReceivedPacket() {
                Interlocked.Increment(ref receivedPacketsCount);
            }
            public static void SentPacket() {
                Interlocked.Increment(ref sentPacketsCount);
            }
        }
    }
}
