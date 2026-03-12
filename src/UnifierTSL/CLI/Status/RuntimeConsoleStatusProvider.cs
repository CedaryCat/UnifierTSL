using System.Collections.Concurrent;
using UnifierTSL.ConsoleClient.Shared.ConsolePrompting;
using UnifierTSL.Events.Handlers;
using UnifierTSL.Performance;
using UnifierTSL.Servers;
using static UnifierTSL.ConsoleClient.Shell.AnsiColorCodec;

namespace UnifierTSL.CLI.Status
{
    internal static class RuntimeConsoleStatusProvider
    {
        private const string HealthyStatusColor = "\u001b[42m";
        private const string WarnStatusColor = "\u001b[48;5;208m";
        private const string BadStatusColor = "\u001b[101m";
        private static readonly TimeSpan LauncherBandwidthWindow = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan LauncherBandwidthRetention = TimeSpan.FromSeconds(2);
        private static readonly ConcurrentQueue<LauncherNetworkSample> LauncherNetworkSamples = new();
        private static readonly string BreathIndicatorFramesSerialized = ConsoleStatusIndicatorFramesCodec.Serialize(["◉", "◎", "◉", "○"]);

        private readonly record struct LauncherNetworkSample(
            DateTimeOffset TimestampUtc,
            ulong ReceivedBytesCount,
            ulong SentBytesCount);

        private readonly record struct LauncherBandwidthSnapshot(
            double UpKBps,
            double DownKBps);

        public static Func<ConsoleStatusResolveContext, ConsoleStatusFrame?> CreateBaseline(ServerContext? server) {
            return context => Compose(server, context);
        }

        private static ConsoleStatusFrame? Compose(ServerContext? server, ConsoleStatusResolveContext context) {
            if (server is null) {
                if (!UnifiedServerCoordinator.Running) {
                    return null;
                }
            }
            else if (!server.IsRunning) {
                return null;
            }

            long frameNumber = context.SampleUtc.ToUnixTimeMilliseconds() / ConsoleStatusService.RefreshIntervalMs;
            ConsoleStatusSettings consoleStatus = UnifierApi.GetConsoleStatus();
            bool useColorfulStatus = UnifierApi.UseColorfulConsoleStatus();

            string text = server is null
                ? ComposeLauncherMonitor(context.SampleUtc, consoleStatus, useColorfulStatus)
                : ComposeServerMonitor(server, frameNumber, consoleStatus, useColorfulStatus);

            ConsoleStatusFrame frame = new(
                Text: text,
                IndicatorFrameIntervalMs: ConsoleStatusService.RefreshIntervalMs,
                IndicatorStylePrefix: useColorfulStatus ? HealthyStatusColor : string.Empty,
                IndicatorFrames: BreathIndicatorFramesSerialized);

            BuildConsoleStatusFrameEvent args = new(
                server: server,
                context: context,
                frame: frame);
            try {
                UnifierApi.EventHub.Launcher.BuildConsoleStatusFrame.Invoke(ref args);
            }
            catch {
            }

            return args.Frame;
        }

        private static string ComposeServerMonitor(
            ServerContext server,
            long frame,
            ConsoleStatusSettings consoleStatus,
            bool useColorfulStatus) {
            return useColorfulStatus
                ? ComposeColorfulServerMonitor(server, frame, consoleStatus)
                : ComposePlainServerMonitor(server, frame, consoleStatus);
        }

        private static string ComposeColorfulServerMonitor(
            ServerContext server,
            long frame,
            ConsoleStatusSettings consoleStatus) {

            var perfData = ServerPerformance.Queries.GetSnapshot(server, TimeSpan.FromSeconds(1));
            var ups = perfData.TicksPerSecond;
            var util = perfData.LoopUtilization;

            double upKBps = perfData.SentBytesCount / 1000d;
            double downKBps = perfData.ReceivedBytesCount / 1000d;

            int online = server.ActivePlayerCount;

            int upsL = UpsLevel(ups, consoleStatus);
            int utilL = UtilLevel(util, consoleStatus);
            ConsoleStatusBandwidthThresholds bandwidthThresholds = consoleStatus.ServerBandwidth;

            if (online == 0) {
                upsL = utilL = -1;
            }


            int onlineL = OnlineLevel(online, server.Main.maxNetPlayers, consoleStatus);
            int upL = BandwidthLevel(upKBps, bandwidthThresholds.UpWarnKBps, bandwidthThresholds.UpBadKBps);
            int downL = BandwidthLevel(downKBps, bandwidthThresholds.DownWarnKBps, bandwidthThresholds.DownBadKBps);

            string text = $"{LColor(onlineL)}[online:{online}/{server.Main.maxNetPlayers}]{Reset} ";
            if (upsL == utilL) {
                text +=
                    $"{LColor(upsL)}[tps:{ups:0.0}" +
                    "|" +
                    $"util:{(util >= 1 ? "1.00" : $"{util:0.0%}")}]{Reset} ";
            }
            else {
                text +=
                    $"{LColor(upsL)}[tps:{ups:0.0}]{Reset}" +
                    $"{LColor(utilL)}[util:{(util >= 1 ? "1.00" : $"{util:0.0%}")}]{Reset} ";
            }

            if (upL == downL) {
                text +=
                    $"{LColor(upL, healthyDefault: true)}[↑{FormatBandwidth(upKBps, consoleStatus)}" +
                    " " +
                    $"↓{FormatBandwidth(downKBps, consoleStatus)}]{Reset}";
            }
            else {
                text +=
                    $"{LColor(upL, healthyDefault: true)}[↑{FormatBandwidth(upKBps, consoleStatus)}]" +
                    $"{LColor(downL, healthyDefault: true)}[↓{FormatBandwidth(downKBps, consoleStatus)}]{Reset}";
            }

            return text;
        }

        private static string ComposePlainServerMonitor(
            ServerContext server,
            long frame,
            ConsoleStatusSettings consoleStatus) {

            var perfData = ServerPerformance.Queries.GetSnapshot(server, TimeSpan.FromSeconds(1));
            var ups = perfData.TicksPerSecond;
            var util = perfData.LoopUtilization;

            double upKBps = perfData.SentBytesCount / 1000d;
            double downKBps = perfData.ReceivedBytesCount / 1000d;

            int online = server.ActivePlayerCount;

            return $"[online:{online}/{server.Main.maxNetPlayers}] "
                + $"[tps:{ups:0.0}|util:{FormatUtil(util)}] "
                + $"[↑{FormatBandwidth(upKBps, consoleStatus)} ↓{FormatBandwidth(downKBps, consoleStatus)}]";
        }

        private static string ComposeLauncherMonitor(
            DateTimeOffset sampleUtc,
            ConsoleStatusSettings consoleStatus,
            bool useColorfulStatus) {
            return useColorfulStatus
                ? ComposeColorfulLauncherMonitor(sampleUtc, consoleStatus)
                : ComposePlainLauncherMonitor(sampleUtc, consoleStatus);
        }

        private static string ComposeColorfulLauncherMonitor(
            DateTimeOffset sampleUtc,
            ConsoleStatusSettings consoleStatus) {
            LauncherBandwidthSnapshot bandwidth = SampleLauncherBandwidth(sampleUtc);
            double upKBps = bandwidth.UpKBps;
            double downKBps = bandwidth.DownKBps;
            ConsoleStatusBandwidthThresholds bandwidthThresholds = consoleStatus.LauncherBandwidth;

            int online = UnifiedServerCoordinator.ActiveConnections;
            int onlineL = OnlineLevel(online, Terraria.Main.maxPlayers, consoleStatus);
            int upL = BandwidthLevel(upKBps, bandwidthThresholds.UpWarnKBps, bandwidthThresholds.UpBadKBps);
            int downL = BandwidthLevel(downKBps, bandwidthThresholds.DownWarnKBps, bandwidthThresholds.DownBadKBps);

            string text = $"{LColor(onlineL)}[online:{online}/{Terraria.Main.maxPlayers}]{Reset} ";
            if (upL == downL) {
                text +=
                    $"{LColor(upL, healthyDefault: true)}[↑{FormatBandwidth(upKBps, consoleStatus)}" +
                    " " +
                    $"↓{FormatBandwidth(downKBps, consoleStatus)}]{Reset}";
            }
            else {
                text +=
                    $"{LColor(upL, healthyDefault: true)}[↑{FormatBandwidth(upKBps, consoleStatus)}]" +
                    $"{LColor(downL, healthyDefault: true)}[↓{FormatBandwidth(downKBps, consoleStatus)}]{Reset}";
            }
            return text;
        }

        private static string ComposePlainLauncherMonitor(
            DateTimeOffset sampleUtc,
            ConsoleStatusSettings consoleStatus) {
            LauncherBandwidthSnapshot bandwidth = SampleLauncherBandwidth(sampleUtc);
            double upKBps = bandwidth.UpKBps;
            double downKBps = bandwidth.DownKBps;

            int online = UnifiedServerCoordinator.ActiveConnections;

            return $"[online:{online}/{Terraria.Main.maxPlayers}] "
                + $"[↑{FormatBandwidth(upKBps, consoleStatus)} ↓{FormatBandwidth(downKBps, consoleStatus)}]";
        }

        private static LauncherBandwidthSnapshot SampleLauncherBandwidth(DateTimeOffset sampleUtc) {
            LauncherNetworkSample currentSample = new(
                TimestampUtc: sampleUtc,
                ReceivedBytesCount: ServerPerformance.Network.ReceivedBytesCount,
                SentBytesCount: ServerPerformance.Network.SentBytesCount);

            LauncherNetworkSamples.Enqueue(currentSample);
            PruneLauncherNetworkSamples(sampleUtc - LauncherBandwidthRetention);

            DateTimeOffset windowStart = sampleUtc - LauncherBandwidthWindow;
            bool hasWindowSample = false;
            LauncherNetworkSample oldestWindowSample = default;
            LauncherNetworkSample latestWindowSample = default;

            foreach (LauncherNetworkSample sample in LauncherNetworkSamples) {
                if (sample.TimestampUtc < windowStart) {
                    continue;
                }

                if (!hasWindowSample) {
                    oldestWindowSample = sample;
                    latestWindowSample = sample;
                    hasWindowSample = true;
                    continue;
                }

                latestWindowSample = sample;
            }

            if (!hasWindowSample || latestWindowSample.TimestampUtc <= oldestWindowSample.TimestampUtc) {
                return default;
            }

            double elapsedSeconds = (latestWindowSample.TimestampUtc - oldestWindowSample.TimestampUtc).TotalSeconds;
            if (elapsedSeconds <= 0d) {
                return default;
            }

            ulong sentBytesDelta = SaturatingSubtract(latestWindowSample.SentBytesCount, oldestWindowSample.SentBytesCount);
            ulong receivedBytesDelta = SaturatingSubtract(latestWindowSample.ReceivedBytesCount, oldestWindowSample.ReceivedBytesCount);

            return new(
                UpKBps: sentBytesDelta / elapsedSeconds / 1000d,
                DownKBps: receivedBytesDelta / elapsedSeconds / 1000d);
        }

        private static void PruneLauncherNetworkSamples(DateTimeOffset retentionCutoffUtc) {
            while (LauncherNetworkSamples.TryPeek(out LauncherNetworkSample sample) &&
                sample.TimestampUtc < retentionCutoffUtc) {
                LauncherNetworkSamples.TryDequeue(out _);
            }
        }

        private static ulong SaturatingSubtract(ulong latest, ulong oldest)
            => latest >= oldest ? latest - oldest : 0;

        private static string FormatUtil(double util) {
            return util >= 1 ? "1.00" : $"{util:0.0%}";
        }

        private static string FormatBandwidth(double kiloBytesPerSecond, ConsoleStatusSettings consoleStatus) {
            double rolloverThreshold = consoleStatus.BandwidthRolloverThreshold;
            double value = consoleStatus.BandwidthUnit == ConsoleStatusBandwidthUnit.Bits
                ? kiloBytesPerSecond * 8d
                : kiloBytesPerSecond;

            string[] units = consoleStatus.BandwidthUnit == ConsoleStatusBandwidthUnit.Bits
                ? ["Kbps", "Mbps", "Gbps", "Tbps"]
                : ["KB/s", "MB/s", "GB/s", "TB/s"];

            int unitIndex = 0;
            while (unitIndex < units.Length - 1 && value >= rolloverThreshold) {
                value /= 1000d;
                unitIndex++;
            }

            string format = unitIndex == 0 ? "0.0" : "0.00";
            return $"{value.ToString(format)}{units[unitIndex]}";
        }

        private static string LColor(int x, int y = -1, bool healthyDefault = false) {
            return Math.Max(x, y) switch {
                -1 => Reset,
                1 => healthyDefault ? Reset : HealthyStatusColor,
                2 => WarnStatusColor,
                _ => BadStatusColor,
            };
        }

        private static int UpsLevel(double ups, ConsoleStatusSettings consoleStatus) {
            double deviation = Math.Abs(ups - consoleStatus.TargetUps);
            if (deviation <= consoleStatus.HealthyUpsDeviation) {
                return 1;
            }

            if (deviation <= consoleStatus.WarningUpsDeviation) {
                return 2;
            }

            return 3;
        }

        private static int UtilLevel(double util, ConsoleStatusSettings consoleStatus) {
            if (util <= consoleStatus.UtilHealthyMax) {
                return 1;
            }

            if (util <= consoleStatus.UtilWarningMax) {
                return 2;
            }

            return 3;
        }

        private static int OnlineLevel(int online, int maxNetPlayers, ConsoleStatusSettings consoleStatus) {
            int remainingSlots = Math.Max(0, maxNetPlayers - online);
            if (remainingSlots <= consoleStatus.OnlineBadRemainingSlots) {
                return 3;
            }
            if (remainingSlots <= consoleStatus.OnlineWarnRemainingSlots) {
                return 2;
            }
            return 1;
        }

        private static int BandwidthLevel(double kiloBytesPerSecond, double warnThresholdKBps, double badThresholdKBps) {
            if (kiloBytesPerSecond >= badThresholdKBps) {
                return 3;
            }

            if (kiloBytesPerSecond >= warnThresholdKBps) {
                return 2;
            }

            return 1;
        }
    }
}
