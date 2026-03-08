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
            double UpKbps,
            double DownKbps);

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
            ConsoleStatusThresholds thresholds = UnifierApi.GetConsoleStatusThresholds();
            bool useColorfulStatus = UnifierApi.UseColorfulConsoleStatus();

            string text = server is null
                ? ComposeLauncherMonitor(context.SampleUtc, thresholds, useColorfulStatus)
                : ComposeServerMonitor(server, frameNumber, thresholds, useColorfulStatus);

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
            ConsoleStatusThresholds thresholds,
            bool useColorfulStatus) {
            return useColorfulStatus
                ? ComposeColorfulServerMonitor(server, frame, thresholds)
                : ComposePlainServerMonitor(server, frame, thresholds);
        }

        private static string ComposeColorfulServerMonitor(
            ServerContext server,
            long frame,
            ConsoleStatusThresholds thresholds) {

            var perfData = PerformanceData.Queries.GetSnapshot(server, TimeSpan.FromSeconds(1));
            var ups = perfData.TicksPerSecond;
            var util = perfData.BusyUtilization;

            double upKbps = perfData.SentBytesCount / 1000d;
            double downKbps = perfData.ReceivedBytesCount / 1000d;

            int online = Math.Max(0, server.ActivePlayerCount);

            int upsL = UpsLevel(ups, thresholds);
            int utilL = UtilLevel(util, thresholds);
            int onlineL = OnlineLevel(online, server.Main.maxNetPlayers, thresholds);
            int upL = BandwidthLevel(upKbps, thresholds.UpWarnKbps, thresholds.UpBadKbps);
            int downL = BandwidthLevel(downKbps, thresholds.DownWarnKbps, thresholds.DownBadKbps);

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
                    $"{LColor(upL, healthyDefault: true)}[↑{upKbps:0.0}kb/s" +
                    " " +
                    $"↓{downKbps:0.0}kb/s]{Reset}";
            }
            else {
                text +=
                    $"{LColor(upL, healthyDefault: true)}[↑{upKbps:0.0}kb/s]" +
                    $"{LColor(downL, healthyDefault: true)}[↓{downKbps:0.0}kb/s]{Reset}";
            }

            return text;
        }

        private static string ComposePlainServerMonitor(
            ServerContext server,
            long frame,
            ConsoleStatusThresholds thresholds) {

            var perfData = PerformanceData.Queries.GetSnapshot(server, TimeSpan.FromSeconds(1));
            var ups = perfData.TicksPerSecond;
            var util = perfData.BusyUtilization;

            double upKbps = perfData.SentBytesCount / 1000d;
            double downKbps = perfData.ReceivedBytesCount / 1000d;

            int online = Math.Max(0, server.ActivePlayerCount);

            return $"[online:{online}/{server.Main.maxNetPlayers}] "
                + $"[tps:{ups:0.0}|util:{FormatUtil(util)}] "
                + $"[↑{upKbps:0.0}kb/s ↓{downKbps:0.0}kb/s]";
        }

        private static string ComposeLauncherMonitor(
            DateTimeOffset sampleUtc,
            ConsoleStatusThresholds thresholds,
            bool useColorfulStatus) {
            return useColorfulStatus
                ? ComposeColorfulLauncherMonitor(sampleUtc, thresholds)
                : ComposePlainLauncherMonitor(sampleUtc, thresholds);
        }

        private static string ComposeColorfulLauncherMonitor(
            DateTimeOffset sampleUtc,
            ConsoleStatusThresholds thresholds) {
            LauncherBandwidthSnapshot bandwidth = SampleLauncherBandwidth(sampleUtc);
            double upKbps = bandwidth.UpKbps;
            double downKbps = bandwidth.DownKbps;

            int online = Math.Max(0, UnifiedServerCoordinator.ActiveConnections);
            int onlineL = OnlineLevel(online, Terraria.Main.maxPlayers, thresholds);
            int upL = BandwidthLevel(upKbps, thresholds.UpWarnKbps, thresholds.UpBadKbps);
            int downL = BandwidthLevel(downKbps, thresholds.DownWarnKbps, thresholds.DownBadKbps);

            string text = $"{LColor(onlineL)}[online:{online}/{Terraria.Main.maxPlayers}]{Reset} ";
            if (upL == downL) {
                text +=
                    $"{LColor(upL, healthyDefault: true)}[↑{upKbps:0.0}kb/s" +
                    " " +
                    $"↓{downKbps:0.0}kb/s]{Reset}";
            }
            else {
                text +=
                    $"{LColor(upL, healthyDefault: true)}[↑{upKbps:0.0}kb/s]" +
                    $"{LColor(downL, healthyDefault: true)}[↓{downKbps:0.0}kb/s]{Reset}";
            }
            return text;
        }

        private static string ComposePlainLauncherMonitor(
            DateTimeOffset sampleUtc,
            ConsoleStatusThresholds thresholds) {
            LauncherBandwidthSnapshot bandwidth = SampleLauncherBandwidth(sampleUtc);
            double upKbps = bandwidth.UpKbps;
            double downKbps = bandwidth.DownKbps;

            int online = Math.Max(0, UnifiedServerCoordinator.ActiveConnections);

            return $"[online:{online}/{Terraria.Main.maxPlayers}] "
                + $"[↑{upKbps:0.0}kb/s ↓{downKbps:0.0}kb/s]";
        }

        private static LauncherBandwidthSnapshot SampleLauncherBandwidth(DateTimeOffset sampleUtc) {
            LauncherNetworkSample currentSample = new(
                TimestampUtc: sampleUtc,
                ReceivedBytesCount: PerformanceData.Network.ReceivedBytesCount,
                SentBytesCount: PerformanceData.Network.SentBytesCount);

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
                UpKbps: sentBytesDelta / elapsedSeconds / 1000d,
                DownKbps: receivedBytesDelta / elapsedSeconds / 1000d);
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

        private static string LColor(int x, int y = 0, bool healthyDefault = false) {
            return Math.Max(x, y) switch {
                1 => healthyDefault ? Reset : HealthyStatusColor,
                2 => WarnStatusColor,
                _ => BadStatusColor,
            };
        }

        private static int UpsLevel(double ups, ConsoleStatusThresholds thresholds) {
            double deviation = Math.Abs(ups - thresholds.TargetUps);
            if (deviation <= thresholds.HealthyUpsDeviation) {
                return 1;
            }

            if (deviation <= thresholds.WarningUpsDeviation) {
                return 2;
            }

            return 3;
        }

        private static int UtilLevel(double util, ConsoleStatusThresholds thresholds) {
            if (util <= thresholds.UtilHealthyMax) {
                return 1;
            }

            if (util <= thresholds.UtilWarningMax) {
                return 2;
            }

            return 3;
        }

        private static int OnlineLevel(int online, int maxNetPlayers, ConsoleStatusThresholds thresholds) {
            int remainingSlots = Math.Max(0, maxNetPlayers - online);
            if (remainingSlots <= thresholds.OnlineBadRemainingSlots) {
                return 3;
            }
            if (remainingSlots <= thresholds.OnlineWarnRemainingSlots) {
                return 2;
            }
            return 1;
        }

        private static int BandwidthLevel(double kbps, double warnThresholdKbps, double badThresholdKbps) {
            if (kbps >= badThresholdKbps) {
                return 3;
            }

            if (kbps >= warnThresholdKbps) {
                return 2;
            }

            return 1;
        }
    }
}
