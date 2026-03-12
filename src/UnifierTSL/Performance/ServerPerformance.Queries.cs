using System.Diagnostics;
using Terraria.Testing;
using UnifierTSL.Extensions;
using UnifierTSL.Servers;

namespace UnifierTSL.Performance
{
    public partial class ServerPerformance
    {
        public static class Queries {
            private struct SnapshotAccumulator
            {
                public long TotalSampledTicks;
                public long NonSleepTicks;
                public long UpdateTicks;
                public long DrawTicks;
                public long PresentTicks;
                public long IdleTicks;
                public long GcTicks;
                public long TotalCompletedFrameTicks;
                public long MaxCompletedFrameTicks;
                public long AllocatedBytes;
                public int SampledFrames;
                public int Gen0Collections;
                public int Gen1Collections;
                public int Gen2Collections;
                public uint ReceivedBytesCount;
                public uint SentBytesCount;
                public ushort ReceivedPacketCount;
                public ushort SentPacketCount;
            }

            public static PerformanceSnapshot GetSnapshot(ServerContext server, TimeSpan window) {
                ArgumentNullException.ThrowIfNull(server);
                return GetSnapshotCore(server, server.DetailedFPS, window);
            }

            public static PerformanceSnapshot GetSnapshot(DetailedFPSSystemContext detailedFps, TimeSpan window) {
                ArgumentNullException.ThrowIfNull(detailedFps);
                return GetSnapshotCore(detailedFps.root.ToServer(), detailedFps, window);
            }

            private static PerformanceSnapshot GetSnapshotCore(ServerContext server, DetailedFPSSystemContext detailedFps, TimeSpan window) {
                ArgumentNullException.ThrowIfNull(server);

                if (window <= TimeSpan.Zero) {
                    throw new ArgumentOutOfRangeException(nameof(window), window, "Window must be greater than zero.");
                }

                long requestedWindowTicks = ToStopwatchTicks(window);
                int currentFrameIndex = detailedFps.newest;
                ServerPerformance.FrameData currentFrameData = server.Performance.FramesData[currentFrameIndex];
                bool includeCurrentFrame = false;
                long latestTimestamp = 0;

                if (ShouldIncludeCurrentFrame(detailedFps)) {
                    long currentTimestamp = Stopwatch.GetTimestamp();
                    if (TryGetFrameBounds(currentFrameData, currentTimestamp, out _, out _)) {
                        includeCurrentFrame = true;
                        latestTimestamp = currentTimestamp;
                    }
                }

                if (!includeCurrentFrame && !TryGetLatestCompletedFrameTimestamp(server, detailedFps, out latestTimestamp)) {
                    return PerformanceSnapshot.Empty(window);
                }

                long cutoffTimestamp = latestTimestamp - requestedWindowTicks;
                SnapshotAccumulator accumulator = default;

                if (includeCurrentFrame) {
                    AccumulateFrameWindow(
                        currentFrameData,
                        TryGetFrameAtSlot(detailedFps, currentFrameIndex),
                        cutoffTimestamp,
                        latestTimestamp,
                        countCompletedFrame: false,
                        ref accumulator);
                }

                foreach (int frameIndex in EnumerateCompletedFrameIndices(detailedFps)) {
                    ServerPerformance.FrameData frameData = server.Performance.FramesData[frameIndex];
                    if (!TryGetCompletedFrameBounds(frameData, out _, out long frameEndTimestamp)) {
                        continue;
                    }

                    if (frameEndTimestamp <= cutoffTimestamp) {
                        break;
                    }

                    AccumulateFrameWindow(
                        frameData,
                        TryGetFrameAtSlot(detailedFps, frameIndex),
                        cutoffTimestamp,
                        frameEndTimestamp,
                        countCompletedFrame: true,
                        ref accumulator);
                }

                if (accumulator.TotalSampledTicks <= 0) {
                    return PerformanceSnapshot.Empty(window);
                }

                TimeSpan sampledDuration = ToTimeSpan(accumulator.TotalSampledTicks);
                TimeSpan averageFrameTime = accumulator.SampledFrames == 0
                    ? TimeSpan.Zero
                    : ToTimeSpan(accumulator.TotalCompletedFrameTicks / accumulator.SampledFrames);

                return new PerformanceSnapshot(
                    RequestedWindow: window,
                    SampleDuration: sampledDuration,
                    HasFullWindow: accumulator.TotalSampledTicks >= requestedWindowTicks,
                    SampledFrames: accumulator.SampledFrames,
                    UpdateTime: ToTimeSpan(accumulator.UpdateTicks),
                    DrawTime: ToTimeSpan(accumulator.DrawTicks),
                    PresentTime: ToTimeSpan(accumulator.PresentTicks),
                    IdleTime: ToTimeSpan(accumulator.IdleTicks),
                    GCPauseTime: ToTimeSpan(accumulator.GcTicks),
                    NonSleepTime: ToTimeSpan(accumulator.NonSleepTicks),
                    AverageFrameTime: averageFrameTime,
                    MaxFrameTime: ToTimeSpan(accumulator.MaxCompletedFrameTicks),
                    AllocatedBytes: accumulator.AllocatedBytes,
                    Gen0Collections: accumulator.Gen0Collections,
                    Gen1Collections: accumulator.Gen1Collections,
                    Gen2Collections: accumulator.Gen2Collections,
                    ReceivedBytesCount: accumulator.ReceivedBytesCount,
                    ReceivedPacketCount: accumulator.ReceivedPacketCount,
                    SentBytesCount: accumulator.SentBytesCount,
                    SentPacketCount: accumulator.SentPacketCount);
            }

            public static double GetBusyUtilization(ServerContext server, TimeSpan window)
                => GetSnapshot(server, window).BusyUtilization;

            public static double GetBusyUtilization(DetailedFPSSystemContext detailedFps, TimeSpan window)
                => GetSnapshot(detailedFps, window).BusyUtilization;

            public static double GetUpdateUtilization(ServerContext server, TimeSpan window)
                => GetSnapshot(server, window).UpdateUtilization;

            public static double GetUpdateUtilization(DetailedFPSSystemContext detailedFps, TimeSpan window)
                => GetSnapshot(detailedFps, window).UpdateUtilization;

            public static double GetFramesPerSecond(ServerContext server, TimeSpan window)
                => GetSnapshot(server, window).FramesPerSecond;

            public static double GetFramesPerSecond(DetailedFPSSystemContext detailedFps, TimeSpan window)
                => GetSnapshot(detailedFps, window).FramesPerSecond;

            public static double GetTicksPerSecond(ServerContext server, TimeSpan window)
                => GetSnapshot(server, window).TicksPerSecond;

            public static double GetTicksPerSecond(DetailedFPSSystemContext detailedFps, TimeSpan window)
                => GetSnapshot(detailedFps, window).TicksPerSecond;

            public static TimeSpan GetAverageFrameDuration(ServerContext server, TimeSpan window)
                => GetSnapshot(server, window).AverageFrameTime;

            public static TimeSpan GetAverageFrameDuration(DetailedFPSSystemContext detailedFps, TimeSpan window)
                => GetSnapshot(detailedFps, window).AverageFrameTime;

            public static TimeSpan GetMaxFrameDuration(ServerContext server, TimeSpan window)
                => GetSnapshot(server, window).MaxFrameTime;

            public static TimeSpan GetMaxFrameDuration(DetailedFPSSystemContext detailedFps, TimeSpan window)
                => GetSnapshot(detailedFps, window).MaxFrameTime;

            public static double GetAllocatedBytesPerSecond(ServerContext server, TimeSpan window)
                => GetSnapshot(server, window).AllocatedBytesPerSecond;

            public static double GetAllocatedBytesPerSecond(DetailedFPSSystemContext detailedFps, TimeSpan window)
                => GetSnapshot(detailedFps, window).AllocatedBytesPerSecond;

            private static DetailedFPS.Frame? TryGetFrameAtSlot(DetailedFPSSystemContext detailedFps, int slot) {
                if (detailedFps.Frames is null || detailedFps.Frames.Length == 0) {
                    return null;
                }

                if ((uint)slot >= (uint)detailedFps.Frames.Length) {
                    return null;
                }

                return detailedFps.Frames[slot];
            }

            private static IEnumerable<int> EnumerateCompletedFrameIndices(DetailedFPSSystemContext detailedFps) {
                int index = detailedFps.newest;
                while (index != detailedFps.oldest) {
                    index--;
                    if (index < 0) {
                        index = detailedFps.Frames.Length - 1;
                    }
                    yield return index;
                }
            }

            private static bool TryGetLatestCompletedFrameTimestamp(ServerContext server, DetailedFPSSystemContext detailedFps, out long timestamp) {
                foreach (int frameIndex in EnumerateCompletedFrameIndices(detailedFps)) {
                    if (TryGetCompletedFrameBounds(server.Performance.FramesData[frameIndex], out _, out timestamp)) {
                        return true;
                    }
                }

                timestamp = 0;
                return false;
            }

            private static bool TryGetCompletedFrameBounds(ServerPerformance.FrameData frameData, out long startTimestamp, out long endTimestamp)
                => TryGetFrameBounds(frameData, frameData.endTimestamp, out startTimestamp, out endTimestamp);

            private static bool TryGetFrameBounds(ServerPerformance.FrameData frameData, long endTimestamp, out long startTimestamp, out long resolvedEndTimestamp) {
                startTimestamp = frameData.beginTimestamp;
                resolvedEndTimestamp = endTimestamp;
                return startTimestamp > 0 && resolvedEndTimestamp > startTimestamp;
            }

            private static long GetBudgetedSleepOverlapTicks(
                ServerPerformance.FrameData frameData,
                long sampledStartTimestamp,
                long sampledEndTimestamp) {
                long budgetedSleepTicks = frameData.budgetedSleepTicks;
                if (budgetedSleepTicks <= 0 || sampledEndTimestamp <= sampledStartTimestamp) {
                    return 0;
                }

                long budgetedFrameTicks = Math.Max(0, frameData.budgetedFrameTicks);
                budgetedSleepTicks = Math.Min(budgetedSleepTicks, budgetedFrameTicks);

                long sleepStartTimestamp = frameData.beginTimestamp + (budgetedFrameTicks - budgetedSleepTicks);
                long sleepEndTimestamp = sleepStartTimestamp + budgetedSleepTicks;
                long overlapStartTimestamp = Math.Max(sampledStartTimestamp, sleepStartTimestamp);
                long overlapEndTimestamp = Math.Min(sampledEndTimestamp, sleepEndTimestamp);

                return overlapEndTimestamp > overlapStartTimestamp
                    ? overlapEndTimestamp - overlapStartTimestamp
                    : 0;
            }

            private static bool HasFrameEvents(DetailedFPS.Frame? frame) {
                if (!frame.HasValue) {
                    return false;
                }

                return HasFrameEvents(frame.Value);
            }

            private static bool HasFrameEvents(DetailedFPS.Frame frame)
                => frame.events is { Count: > 0 };

            private static bool ShouldIncludeCurrentFrame(DetailedFPSSystemContext detailedFps)
                => !detailedFps.root.Main.dedServ || detailedFps.root.Netplay.HasClients;

            private static void AccumulateFrameWindow(
                ServerPerformance.FrameData frameData,
                DetailedFPS.Frame? frame,
                long cutoffTimestamp,
                long frameEndTimestamp,
                bool countCompletedFrame,
                ref SnapshotAccumulator accumulator) {
                if (!TryGetFrameBounds(frameData, frameEndTimestamp, out long frameStartTimestamp, out long resolvedFrameEndTimestamp)) {
                    return;
                }

                if (resolvedFrameEndTimestamp <= cutoffTimestamp) {
                    return;
                }

                long overlapStartTimestamp = Math.Max(frameStartTimestamp, cutoffTimestamp);
                long sampledTicks = resolvedFrameEndTimestamp - overlapStartTimestamp;
                if (sampledTicks <= 0) {
                    return;
                }

                accumulator.TotalSampledTicks += sampledTicks;
                accumulator.NonSleepTicks += sampledTicks - GetBudgetedSleepOverlapTicks(
                    frameData,
                    overlapStartTimestamp,
                    resolvedFrameEndTimestamp);

                accumulator.ReceivedBytesCount += frameData.ReceivedBytesCount;
                accumulator.ReceivedPacketCount += frameData.ReceivedPacketCount;
                accumulator.SentBytesCount += frameData.SentBytesCount;
                accumulator.SentPacketCount += frameData.SentPacketCount;

                if (HasFrameEvents(frame)) {
                    AccumulateFrameEvents(
                        frame!.Value,
                        frameStartTimestamp,
                        resolvedFrameEndTimestamp,
                        cutoffTimestamp,
                        ref accumulator);
                }

                if (!countCompletedFrame) {
                    return;
                }

                long frameDuration = resolvedFrameEndTimestamp - frameStartTimestamp;
                if (frameDuration <= 0) {
                    return;
                }

                accumulator.SampledFrames++;
                accumulator.TotalCompletedFrameTicks += frameDuration;
                accumulator.MaxCompletedFrameTicks = Math.Max(accumulator.MaxCompletedFrameTicks, frameDuration);

                if (!frame.HasValue) {
                    return;
                }

                DetailedFPS.Frame resolvedFrame = frame.Value;
                accumulator.AllocatedBytes += resolvedFrame.Allocated;

                if (resolvedFrame.CollectionCount is { Length: > 0 }) {
                    accumulator.Gen0Collections += resolvedFrame.CollectionCount[0];
                    if (resolvedFrame.CollectionCount.Length > 1) {
                        accumulator.Gen1Collections += resolvedFrame.CollectionCount[1];
                    }
                    if (resolvedFrame.CollectionCount.Length > 2) {
                        accumulator.Gen2Collections += resolvedFrame.CollectionCount[2];
                    }
                }
            }

            private static void AccumulateFrameEvents(
                DetailedFPS.Frame frame,
                long frameStartTimestamp,
                long frameEndTimestamp,
                long cutoffTimestamp,
                ref SnapshotAccumulator accumulator) {
                if (!HasFrameEvents(frame)) {
                    return;
                }

                List<DetailedFPS.Frame.Event> events = frame.events!;
                for (int i = 1; i < events.Count; i++) {
                    DetailedFPS.Frame.Event previousEvent = events[i - 1];
                    DetailedFPS.Frame.Event currentEvent = events[i];
                    AccumulateSegment(
                        previousEvent.category,
                        previousEvent.timestamp,
                        currentEvent.timestamp,
                        frameStartTimestamp,
                        frameEndTimestamp,
                        cutoffTimestamp,
                        ref accumulator);
                }

                DetailedFPS.Frame.Event lastEvent = events[^1];
                if (frameEndTimestamp > lastEvent.timestamp && lastEvent.category != DetailedFPS.OperationCategory.End) {
                    AccumulateSegment(
                        lastEvent.category,
                        lastEvent.timestamp,
                        frameEndTimestamp,
                        frameStartTimestamp,
                        frameEndTimestamp,
                        cutoffTimestamp,
                        ref accumulator);
                }
            }

            private static void AccumulateSegment(
                DetailedFPS.OperationCategory category,
                long segmentStartTimestamp,
                long segmentEndTimestamp,
                long frameStartTimestamp,
                long frameEndTimestamp,
                long cutoffTimestamp,
                ref SnapshotAccumulator accumulator) {
                long clippedStartTimestamp = Math.Max(segmentStartTimestamp, frameStartTimestamp);
                long clippedEndTimestamp = Math.Min(segmentEndTimestamp, frameEndTimestamp);
                if (clippedEndTimestamp <= clippedStartTimestamp || clippedEndTimestamp <= cutoffTimestamp) {
                    return;
                }

                long overlapStartTimestamp = Math.Max(clippedStartTimestamp, cutoffTimestamp);
                long segmentTicks = clippedEndTimestamp - overlapStartTimestamp;
                if (segmentTicks <= 0) {
                    return;
                }

                switch (category) {
                    case DetailedFPS.OperationCategory.Update:
                        accumulator.UpdateTicks += segmentTicks;
                        break;
                    case DetailedFPS.OperationCategory.Draw:
                        accumulator.DrawTicks += segmentTicks;
                        break;
                    case DetailedFPS.OperationCategory.Present:
                        accumulator.PresentTicks += segmentTicks;
                        break;
                    case DetailedFPS.OperationCategory.Idle:
                        accumulator.IdleTicks += segmentTicks;
                        break;
                    case DetailedFPS.OperationCategory.GC:
                        accumulator.GcTicks += segmentTicks;
                        break;
                }
            }

            private static long ToStopwatchTicks(TimeSpan timeSpan)
                => (long)(timeSpan.Ticks * (double)Stopwatch.Frequency / TimeSpan.TicksPerSecond);

            private static TimeSpan ToTimeSpan(long stopwatchTicks)
                => TimeSpan.FromTicks((long)(stopwatchTicks * (double)TimeSpan.TicksPerSecond / Stopwatch.Frequency));
        }
    }
}
