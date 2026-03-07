using Microsoft.Xna.Framework;
using NuGet.Protocol.Plugins;
using UnifierTSL.ConsoleClient.Shared.ConsolePrompting;
using UnifierTSL.Events.Handlers;
using UnifierTSL.Servers;
using static UnifierTSL.ConsoleClient.Shell.AnsiColorCodec;

namespace UnifierTSL.CLI.Status
{
    internal static class RuntimeConsoleStatusProvider
    {
        private const string HealthyStatusColor = "\u001b[42m";
        private const string WarnStatusColor = "\u001b[48;5;208m";
        private const string BadStatusColor = "\u001b[101m";
        private static readonly string BreathIndicatorFramesSerialized = ConsoleStatusIndicatorFramesCodec.Serialize(["◉", "◎", "◉", "○"]);

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
                ? ComposeLauncherMonitor(frameNumber, thresholds, useColorfulStatus)
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
            int seed = Math.Abs(server.Name.GetHashCode(StringComparison.Ordinal));
            double phase = (frame * 0.17) + (seed * 0.01);
            phase /= 10;

            double ups = thresholds.TargetUps
                + (Math.Sin(phase) * 8.5)
                + (Math.Sin((phase * 2.3) + 0.6) * 4.0);
            double upsOffset = ups - thresholds.TargetUps;
            double util = upsOffset > 2
                ? 1
                : Math.Min(1, MathHelper.Lerp(0.3f, 1f, (float)Math.Clamp(upsOffset, -2, 2) / 4f + 0.5f) + Math.Sin(phase) * 0.05);
            double upKbps = 48 + Math.Abs(Math.Sin(phase * 0.73)) * 30;
            double downKbps = 64 + Math.Abs(Math.Cos(phase * 0.81)) * 42;
            int online = Math.Max(0, server.ActivePlayers);

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
            int seed = Math.Abs(server.Name.GetHashCode(StringComparison.Ordinal));
            double phase = (frame * 0.17) + (seed * 0.01);
            phase /= 10;

            double ups = thresholds.TargetUps
                + (Math.Sin(phase) * 8.5)
                + (Math.Sin((phase * 2.3) + 0.6) * 4.0);
            double upsOffset = ups - thresholds.TargetUps;
            double util = upsOffset > 2
                ? 1
                : Math.Min(1, MathHelper.Lerp(0.3f, 1f, (float)Math.Clamp(upsOffset, -2, 2) / 4f + 0.5f) + Math.Sin(phase) * 0.05);
            double upKbps = 48 + Math.Abs(Math.Sin(phase * 0.73)) * 30;
            double downKbps = 64 + Math.Abs(Math.Cos(phase * 0.81)) * 42;
            int online = Math.Max(0, server.ActivePlayers);

            return $"[online:{online}/{server.Main.maxNetPlayers}] "
                + $"[tps:{ups:0.0}|util:{FormatUtil(util)}] "
                + $"[up:{upKbps:0.0}kb/s down:{downKbps:0.0}kb/s]";
        }

        private static string ComposeLauncherMonitor(
            long frame,
            ConsoleStatusThresholds thresholds,
            bool useColorfulStatus) {
            return useColorfulStatus
                ? ComposeColorfulLauncherMonitor(frame, thresholds)
                : ComposePlainLauncherMonitor(frame, thresholds);
        }

        private static string ComposeColorfulLauncherMonitor(
            long frame,
            ConsoleStatusThresholds thresholds) {
            double phase = frame * 0.14;
            double upKbps = 40 + Math.Abs(Math.Sin(phase * 0.71)) * 32;
            double downKbps = 52 + Math.Abs(Math.Cos(phase * 0.88)) * 45;

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
            long frame,
            ConsoleStatusThresholds thresholds) {
            double phase = frame * 0.14;
            double upKbps = 40 + Math.Abs(Math.Sin(phase * 0.71)) * 32;
            double downKbps = 52 + Math.Abs(Math.Cos(phase * 0.88)) * 45;
            int online = Math.Max(0, UnifiedServerCoordinator.ActiveConnections);

            return $"[online:{online}/{Terraria.Main.maxPlayers}] "
                + $"[up:{upKbps:0.0}kb/s down:{downKbps:0.0}kb/s]";
        }

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
