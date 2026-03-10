using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Diagnostics;
using Terraria;
using Terraria.Net;
using Terraria.Net.Sockets;
using Terraria.Testing;
using UnifierTSL.Extensions;
using UnifierTSL.Servers;

namespace UnifierTSL.Performance
{
    public class ServerPerformance(ServerContext server) {
        public struct FrameData
        {
            public long beginTimestamp;
            public long endTimestamp;
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
    public static class PerformanceData
    {
        public static class Initializer
        {
            public static void Load() { }
            static Initializer() {
                On.Terraria.Testing.DetailedFPSSystemContext.StartNextFrame += DetailedFPSSystemContext_StartNextFrame;
                IL.OTAPI.HooksSystemContext.NetMessageSystemContext.InvokeSendBytes += NetMessageSystemContext_InvokeSendBytes;

                IL.Terraria.Net.NetManager.mfwh_Broadcast_NetPacket_BroadcastCondition_int += NetManager_DetourSendData;
                IL.Terraria.Net.NetManager.mfwh_Broadcast_NetPacket_int += NetManager_DetourSendData;
                IL.Terraria.Net.NetManager.mfwh_SendToClient += NetManager_DetourSendData;
            }

            private static void NetManager_DetourSendData(MonoMod.Cil.ILContext il) {
                var call = il.Instrs.First(i => i.Operand is MethodReference { Name: nameof(NetManager.SendData) });
                if (call.Previous.Previous.Previous.OpCode.Code is not Code.Ldelem_Ref ||
                    call.Previous.Previous.Operand is not FieldReference { Name: nameof(Terraria.RemoteClient.Socket) }) {
                    throw new InvalidOperationException();
                }

                var inst = call.Previous.Previous;
                inst.OpCode = OpCodes.Nop;
                inst.Operand = null;
                inst = inst.Previous;
                inst.OpCode = OpCodes.Nop;
                inst.Operand = null;

                call.OpCode = OpCodes.Call;
                call.Operand = il.Import(typeof(Initializer).GetMethod(nameof(SendPacket), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static) ?? throw new InvalidOperationException());
            }
            static void SendPacket(NetManager netmanager, RemoteClient[] clients, int clientId, NetPacket packet) {
                netmanager.SendData(clients[clientId].Socket, packet);
                UnifiedServerCoordinator.clientSenders[clientId].CountSentBytes((uint)packet.Writer.BaseStream.Position);
            }


            private static void NetMessageSystemContext_InvokeSendBytes(MonoMod.Cil.ILContext il) {
                var call = il.Instrs.First(i => i.Operand is MethodReference { Name: nameof(ISocket.AsyncSend) });
                il.IL.InsertBefore(call, Instruction.Create(OpCodes.Ldarg_S, il.Method.Parameters.First(p => p.Name is "remoteClient")));
                call.OpCode = OpCodes.Call;
                call.Operand = il.Import(typeof(Initializer).GetMethod(nameof(AsyncSend), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static) ?? throw new InvalidOperationException());
            }

            static void AsyncSend(ISocket socket, byte[] data, int offset, int size, SocketSendCallback callback, object state, int clientId) {
                socket.AsyncSend(data, offset, size, callback, state);
                UnifiedServerCoordinator.clientSenders[clientId].CountSentBytes((uint)size);
            }

            static void DetailedFPSSystemContext_StartNextFrame(On.Terraria.Testing.DetailedFPSSystemContext.orig_StartNextFrame orig, DetailedFPSSystemContext self) {
                var server = self.root.ToServer();
                var perf = server.Performance;

                perf.FramesData[self.newest].Finish(server);
                orig(self);
                perf.FramesData[self.newest].Begin(server);
            }
        }

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
        public static class Queries {


            private struct SnapshotAccumulator
            {
                public long TotalSampledTicks;
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
                return GetSnapshot(server.DetailedFPS, window);
            }

            public static PerformanceSnapshot GetSnapshot(DetailedFPSSystemContext detailedFps, TimeSpan window) {
                ArgumentNullException.ThrowIfNull(detailedFps);

                if (window <= TimeSpan.Zero) {
                    throw new ArgumentOutOfRangeException(nameof(window), window, "Window must be greater than zero.");
                }

                DetailedFPS.Frame? currentFrame = TryGetCurrentFrame(detailedFps);
                bool hasCurrentFrameData = HasFrameEvents(currentFrame);
                bool includeCurrentFrame = hasCurrentFrameData && ShouldIncludeCurrentFrame(detailedFps);
                long latestTimestamp;

                if (includeCurrentFrame) {
                    latestTimestamp = Stopwatch.GetTimestamp();
                }
                else if (!TryGetLatestCompletedFrameTimestamp(detailedFps, out latestTimestamp)) {
                    return PerformanceSnapshot.Empty(window);
                }

                long requestedWindowTicks = ToStopwatchTicks(window);
                long cutoffTimestamp = latestTimestamp - requestedWindowTicks;
                SnapshotAccumulator accumulator = default;

                if (includeCurrentFrame) {
                    AccumulateFrameWindow(
                        currentFrame!.Value,
                        cutoffTimestamp,
                        latestTimestamp,
                        countCompletedFrame: false,
                        ref accumulator);
                }

                foreach (DetailedFPS.Frame frame in detailedFps.EnumerateFrames()) {
                    if (!TryGetFrameBounds(frame, out _, out long frameEndTimestamp)) {
                        continue;
                    }

                    if (frameEndTimestamp <= cutoffTimestamp) {
                        break;
                    }

                    AccumulateFrameWindow(
                        frame,
                        cutoffTimestamp,
                        frameEndTimestamp,
                        countCompletedFrame: true,
                        ref accumulator);
                }

                var server = detailedFps.root.ToServer();
                foreach (ServerPerformance.FrameData frame in server.Performance.EnumerateFrames(server)) {
                    if (frame.endTimestamp <= cutoffTimestamp) {
                        break;
                    }

                    accumulator.ReceivedBytesCount += frame.ReceivedBytesCount;
                    accumulator.ReceivedPacketCount += frame.ReceivedPacketCount;
                    accumulator.SentBytesCount += frame.SentBytesCount;
                    accumulator.SentPacketCount += frame.SentPacketCount;
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

            private static DetailedFPS.Frame? TryGetCurrentFrame(DetailedFPSSystemContext detailedFps) {
                if (detailedFps.Frames is null || detailedFps.Frames.Length == 0) {
                    return null;
                }

                int newest = detailedFps.newest;
                if ((uint)newest >= (uint)detailedFps.Frames.Length) {
                    return null;
                }

                return detailedFps.Frames[newest];
            }

            private static bool TryGetLatestCompletedFrameTimestamp(DetailedFPSSystemContext detailedFps, out long timestamp) {
                foreach (DetailedFPS.Frame frame in detailedFps.EnumerateFrames()) {
                    if (TryGetFrameBounds(frame, out _, out timestamp)) {
                        return true;
                    }
                }

                timestamp = 0;
                return false;
            }

            private static bool TryGetFrameBounds(DetailedFPS.Frame frame, out long startTimestamp, out long endTimestamp) {
                if (!HasFrameEvents(frame) || frame.events!.Count < 2) {
                    startTimestamp = 0;
                    endTimestamp = 0;
                    return false;
                }

                startTimestamp = frame.events[0].timestamp;
                endTimestamp = frame.events[^1].timestamp;
                return endTimestamp > startTimestamp;
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
                DetailedFPS.Frame frame,
                long cutoffTimestamp,
                long finalTimestamp,
                bool countCompletedFrame,
                ref SnapshotAccumulator accumulator) {
                if (!HasFrameEvents(frame)) {
                    return;
                }

                List<DetailedFPS.Frame.Event> events = frame.events!;
                for (int i = 1; i < events.Count; i++) {
                    DetailedFPS.Frame.Event previousEvent = events[i - 1];
                    DetailedFPS.Frame.Event currentEvent = events[i];
                    AccumulateSegment(previousEvent.category, previousEvent.timestamp, currentEvent.timestamp, cutoffTimestamp, ref accumulator);
                }

                DetailedFPS.Frame.Event lastEvent = events[^1];
                if (finalTimestamp > lastEvent.timestamp && lastEvent.category != DetailedFPS.OperationCategory.End) {
                    AccumulateSegment(lastEvent.category, lastEvent.timestamp, finalTimestamp, cutoffTimestamp, ref accumulator);
                }

                if (!countCompletedFrame || events.Count < 2) {
                    return;
                }

                long frameStartTimestamp = events[0].timestamp;
                long frameDuration = finalTimestamp - frameStartTimestamp;
                if (frameDuration <= 0 || finalTimestamp <= cutoffTimestamp) {
                    return;
                }

                accumulator.SampledFrames++;
                accumulator.TotalCompletedFrameTicks += frameDuration;
                accumulator.MaxCompletedFrameTicks = Math.Max(accumulator.MaxCompletedFrameTicks, frameDuration);
                accumulator.AllocatedBytes += frame.Allocated;

                if (frame.CollectionCount is { Length: > 0 }) {
                    accumulator.Gen0Collections += frame.CollectionCount[0];
                    if (frame.CollectionCount.Length > 1) {
                        accumulator.Gen1Collections += frame.CollectionCount[1];
                    }
                    if (frame.CollectionCount.Length > 2) {
                        accumulator.Gen2Collections += frame.CollectionCount[2];
                    }
                }
            }

            private static void AccumulateSegment(
                DetailedFPS.OperationCategory category,
                long segmentStartTimestamp,
                long segmentEndTimestamp,
                long cutoffTimestamp,
                ref SnapshotAccumulator accumulator) {
                if (segmentEndTimestamp <= segmentStartTimestamp || segmentEndTimestamp <= cutoffTimestamp) {
                    return;
                }

                long overlapStartTimestamp = Math.Max(segmentStartTimestamp, cutoffTimestamp);
                long segmentTicks = segmentEndTimestamp - overlapStartTimestamp;
                if (segmentTicks <= 0) {
                    return;
                }

                accumulator.TotalSampledTicks += segmentTicks;
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
