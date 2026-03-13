using System.Diagnostics;
using Terraria.Testing;
using UnifierTSL.Servers;

namespace UnifierTSL.Performance
{
    public partial class ServerPerformance(ServerContext server) {
        public struct FrameData
        {
            public long beginTimestamp;
            public long endTimestamp;
            public long timingDriftTicks;
            public long budgetedFrameTicks;
            public long budgetedSleepTicks;
            public uint ReceivedBytesCount;
            public uint SentBytesCount;
            public ushort ReceivedPacketCount;
            public ushort SentPacketCount;
            public void Finish(ServerContext server) {
                var perf = server.Performance;
                perf.TotalReceivedBytesCount += ReceivedBytesCount;
                perf.TotalSentBytesCount += SentBytesCount;
                perf.TotalReceivedPacketCount += ReceivedPacketCount;
                perf.TotalSentPacketCount += SentPacketCount;
                endTimestamp = Stopwatch.GetTimestamp();
            }
            public void Begin(ServerContext server) {
                this = default;
                beginTimestamp = Stopwatch.GetTimestamp();
            }
            public void SetBudget(double timingDriftMs, double budgetedFrameMs, double budgetedSleepMs) {
                timingDriftTicks = MillisecondsToStopwatchTicks(timingDriftMs);
                budgetedFrameTicks = MillisecondsToStopwatchTicks(budgetedFrameMs);
                budgetedSleepTicks = Math.Min(
                    budgetedFrameTicks,
                    MillisecondsToStopwatchTicks(budgetedSleepMs));
            }
            private static long MillisecondsToStopwatchTicks(double milliseconds)
                => milliseconds <= 0d ? 0 : (long)(milliseconds * Stopwatch.Frequency / 1000d);
        }
        public readonly FrameData[] FramesData = new FrameData[DetailedFPS.FrameCount];
        public ref FrameData CurrentFrameData => ref FramesData[server.DetailedFPS.newest];
        public ulong TotalReceivedBytesCount { get; private set; }
        public ulong TotalSentBytesCount { get; private set; }
        public uint TotalReceivedPacketCount { get; private set; }
        public uint TotalSentPacketCount { get; private set; }
        public IEnumerable<FrameData> EnumerateFrames(ServerContext server) {
            var fps = server.DetailedFPS;
            int k = fps.newest;
            while (k != fps.oldest) {
                int num = k - 1;
                k = num;
                if (num < 0) {
                    k = fps.Frames.Length - 1;
                }
                yield return FramesData[k];
            }
        }
    }
}
