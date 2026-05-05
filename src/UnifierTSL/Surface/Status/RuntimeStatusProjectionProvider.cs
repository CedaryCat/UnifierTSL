using System.Collections.Concurrent;
using UnifierTSL.Surface;
using UnifierTSL.Contracts.Display;
using UnifierTSL.Contracts.Projection;
using UnifierTSL.Surface.Prompting.Model;
using UnifierTSL.Events.Handlers;
using UnifierTSL.Performance;
using UnifierTSL.Servers;
using SessionStatusSchema = UnifierTSL.Contracts.Projection.BuiltIn.SessionStatusProjectionSchema;

namespace UnifierTSL.Surface.Status
{
    internal static class RuntimeStatusProjectionProvider
    {
        private static readonly TimeSpan LauncherBandwidthWindow = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan LauncherBandwidthRetention = TimeSpan.FromSeconds(2);
        private static readonly ConcurrentQueue<LauncherNetworkSample> LauncherNetworkSamples = new();
        private static readonly StyledTextLine[] BreathIndicatorFrames = [
            BuildIndicatorFrame("[◉]"),
            BuildIndicatorFrame("[◎]"),
            BuildIndicatorFrame("[◉]"),
            BuildIndicatorFrame("[○]"),
        ];
        private const int BreathIndicatorFrameStepTicks = 15;

        private readonly record struct LauncherNetworkSample(
            DateTimeOffset TimestampUtc,
            ulong ReceivedBytesCount,
            ulong SentBytesCount);

        private readonly record struct LauncherBandwidthSnapshot(
            double UpKBps,
            double DownKBps);

        internal static ProjectionDocument? Compose(ServerContext? server, DateTimeOffset sampleUtc) {
            if (server is null) {
                if (UnifierApi.CurrentPhase < UnifierApi.LifecyclePhase.Running) {
                    return null;
                }
            }
            else if (!server.IsRunning) {
                return null;
            }

            var consoleStatus = SurfaceRuntimeOptions.StatusSettings;
            var line = server is null
                ? ComposeLauncherMonitor(sampleUtc, consoleStatus)
                : ComposeServerMonitor(server, consoleStatus);

            var document = StatusProjectionDocumentFactory.Create(
                nodes: [
                    StatusProjectionDocumentFactory.CreateTextNode(
                        SessionStatusSchema.IndicatorNodeId,
                        animation: StatusProjectionDocumentFactory.CreateAnimation(
                            BreathIndicatorFrameStepTicks,
                            BreathIndicatorFrames)),
                    StatusProjectionDocumentFactory.CreateTextNode(
                        SessionStatusSchema.TitleNodeId,
                        StatusProjectionDocumentFactory.CreateTitleBlock()),
                    StatusProjectionDocumentFactory.CreateTextNode(
                        SessionStatusSchema.HeaderNodeId,
                        StatusProjectionDocumentFactory.ToBlock(line, SurfaceStyleCatalog.StatusBand)),
                ]);

            BuildSurfaceStatusDocumentEvent args = new(
                server: server,
                document: document);
            try {
                UnifierApi.EventHub.Launcher.BuildSurfaceStatusDocument.Invoke(ref args);
            }
            catch {
            }

            return StatusProjectionDocumentFactory.HasVisibleContent(args.Document)
                ? args.Document
                : null;
        }

        private static StyledTextLine ComposeServerMonitor(ServerContext server, StatusProjectionSettings consoleStatus) {
            var perfData = ServerPerformance.Queries.GetSnapshot(server, TimeSpan.FromSeconds(1));
            var ups = perfData.TicksPerSecond;
            var util = perfData.LoopUtilization;
            var upKBps = perfData.SentBytesCount / 1000d;
            var downKBps = perfData.ReceivedBytesCount / 1000d;
            var online = server.ActivePlayerCount;
            var upsL = UpsLevel(ups, consoleStatus);
            var utilL = UtilLevel(util, consoleStatus);
            var bandwidthThresholds = consoleStatus.ServerBandwidth;

            if (online == 0) {
                upsL = utilL = -1;
            }

            var onlineL = OnlineLevel(online, server.Main.maxNetPlayers, consoleStatus);
            var upL = BandwidthLevel(upKBps, bandwidthThresholds.UpWarnKBps, bandwidthThresholds.UpBadKBps);
            var downL = BandwidthLevel(downKBps, bandwidthThresholds.DownWarnKBps, bandwidthThresholds.DownBadKBps);
            StyledTextLineBuilder line = new();

            line.Append($"[online:{online}/{server.Main.maxNetPlayers}]", ResolveHeaderStyleId(onlineL));
            line.Append(" ");
            if (upsL == utilL) {
                line.Append($"[tps:{ups:0.0}|util:{FormatUtil(util)}]", ResolveHeaderStyleId(upsL));
            }
            else {
                line.Append($"[tps:{ups:0.0}]", ResolveHeaderStyleId(upsL));
                line.Append($"[util:{FormatUtil(util)}]", ResolveHeaderStyleId(utilL));
            }

            line.Append(" ");
            if (upL == downL) {
                line.Append(
                    $"[↑{FormatBandwidth(upKBps, consoleStatus)} ↓{FormatBandwidth(downKBps, consoleStatus)}]",
                    ResolveHeaderStyleId(upL, healthyDefault: true));
            }
            else {
                line.Append($"[↑{FormatBandwidth(upKBps, consoleStatus)}]", ResolveHeaderStyleId(upL, healthyDefault: true));
                line.Append($"[↓{FormatBandwidth(downKBps, consoleStatus)}]", ResolveHeaderStyleId(downL, healthyDefault: true));
            }

            return line.Build();
        }

        private static StyledTextLine ComposeLauncherMonitor(DateTimeOffset sampleUtc, StatusProjectionSettings consoleStatus) {
            var bandwidth = SampleLauncherBandwidth(sampleUtc);
            var upKBps = bandwidth.UpKBps;
            var downKBps = bandwidth.DownKBps;
            var bandwidthThresholds = consoleStatus.LauncherBandwidth;
            var online = UnifiedServerCoordinator.ActiveConnections;
            var onlineL = OnlineLevel(online, Terraria.Main.maxPlayers, consoleStatus);
            var upL = BandwidthLevel(upKBps, bandwidthThresholds.UpWarnKBps, bandwidthThresholds.UpBadKBps);
            var downL = BandwidthLevel(downKBps, bandwidthThresholds.DownWarnKBps, bandwidthThresholds.DownBadKBps);
            StyledTextLineBuilder line = new();

            line.Append($"[online:{online}/{Terraria.Main.maxPlayers}]", ResolveHeaderStyleId(onlineL));
            line.Append(" ");
            if (upL == downL) {
                line.Append(
                    $"[↑{FormatBandwidth(upKBps, consoleStatus)} ↓{FormatBandwidth(downKBps, consoleStatus)}]",
                    ResolveHeaderStyleId(upL, healthyDefault: true));
            }
            else {
                line.Append($"[↑{FormatBandwidth(upKBps, consoleStatus)}]", ResolveHeaderStyleId(upL, healthyDefault: true));
                line.Append($"[↓{FormatBandwidth(downKBps, consoleStatus)}]", ResolveHeaderStyleId(downL, healthyDefault: true));
            }

            return line.Build();
        }

        private static LauncherBandwidthSnapshot SampleLauncherBandwidth(DateTimeOffset sampleUtc) {
            LauncherNetworkSample currentSample = new(
                TimestampUtc: sampleUtc,
                ReceivedBytesCount: ServerPerformance.Network.ReceivedBytesCount,
                SentBytesCount: ServerPerformance.Network.SentBytesCount);

            LauncherNetworkSamples.Enqueue(currentSample);
            PruneLauncherNetworkSamples(sampleUtc - LauncherBandwidthRetention);

            var windowStart = sampleUtc - LauncherBandwidthWindow;
            var hasWindowSample = false;
            LauncherNetworkSample oldestWindowSample = default;
            LauncherNetworkSample latestWindowSample = default;

            foreach (var sample in LauncherNetworkSamples) {
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

            var elapsedSeconds = (latestWindowSample.TimestampUtc - oldestWindowSample.TimestampUtc).TotalSeconds;
            if (elapsedSeconds <= 0d) {
                return default;
            }

            var sentBytesDelta = SaturatingSubtract(latestWindowSample.SentBytesCount, oldestWindowSample.SentBytesCount);
            var receivedBytesDelta = SaturatingSubtract(latestWindowSample.ReceivedBytesCount, oldestWindowSample.ReceivedBytesCount);

            return new(
                UpKBps: sentBytesDelta / elapsedSeconds / 1000d,
                DownKBps: receivedBytesDelta / elapsedSeconds / 1000d);
        }

        private static void PruneLauncherNetworkSamples(DateTimeOffset retentionCutoffUtc) {
            while (LauncherNetworkSamples.TryPeek(out var sample) &&
                sample.TimestampUtc < retentionCutoffUtc) {
                LauncherNetworkSamples.TryDequeue(out _);
            }
        }

        private static ulong SaturatingSubtract(ulong latest, ulong oldest)
            => latest >= oldest ? latest - oldest : 0;

        private static string FormatUtil(double util) {
            return util >= 1 ? "1.00" : $"{util:0.0%}";
        }

        private static string FormatBandwidth(double kiloBytesPerSecond, StatusProjectionSettings consoleStatus) {
            var rolloverThreshold = consoleStatus.BandwidthRolloverThreshold;
            var value = consoleStatus.BandwidthUnit == StatusProjectionBandwidthUnit.Bits
                ? kiloBytesPerSecond * 8d
                : kiloBytesPerSecond;

            string[] units = consoleStatus.BandwidthUnit == StatusProjectionBandwidthUnit.Bits
                ? ["Kbps", "Mbps", "Gbps", "Tbps"]
                : ["KB/s", "MB/s", "GB/s", "TB/s"];

            var unitIndex = 0;
            while (unitIndex < units.Length - 1 && value >= rolloverThreshold) {
                value /= 1000d;
                unitIndex++;
            }

            var format = unitIndex == 0 ? "0.0" : "0.00";
            return $"{value.ToString(format)}{units[unitIndex]}";
        }

        private static string ResolveHeaderStyleId(int x, int y = -1, bool healthyDefault = false) {
            return Math.Max(x, y) switch {
                1 when !healthyDefault => SurfaceStyleCatalog.StatusHeaderPositive,
                2 => SurfaceStyleCatalog.StatusHeaderWarning,
                3 => SurfaceStyleCatalog.StatusHeaderNegative,
                _ => string.Empty,
            };
        }

        private static int UpsLevel(double ups, StatusProjectionSettings consoleStatus) {
            var deviation = Math.Abs(ups - consoleStatus.TargetUps);
            if (deviation <= consoleStatus.HealthyUpsDeviation) {
                return 1;
            }

            if (deviation <= consoleStatus.WarningUpsDeviation) {
                return 2;
            }

            return 3;
        }

        private static int UtilLevel(double util, StatusProjectionSettings consoleStatus) {
            if (util <= consoleStatus.UtilHealthyMax) {
                return 1;
            }

            if (util <= consoleStatus.UtilWarningMax) {
                return 2;
            }

            return 3;
        }

        private static int OnlineLevel(int online, int maxNetPlayers, StatusProjectionSettings consoleStatus) {
            var remainingSlots = Math.Max(0, maxNetPlayers - online);
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

        private static StyledTextLine BuildIndicatorFrame(string indicatorText) {
            return new StyledTextLine {
                Runs = string.IsNullOrEmpty(indicatorText)
                    ? []
                    : [
                        new StyledTextRun {
                            Text = indicatorText,
                            StyleId = SurfaceStyleCatalog.StatusHeaderIndicator,
                        },
                    ],
            };
        }
    }
}
