using System.Text.Json.Serialization;
using UnifierTSL.Surface.Status;

namespace UnifierTSL
{
    internal static class LauncherPortRules
    {
        public static bool IsValidListenPort(int port) {
            return (uint)port <= ushort.MaxValue;
        }
    }

    internal enum LogPersistenceMode
    {
        Txt,
        None,
        Sqlite,
    }

    internal enum JoinServerMode
    {
        None,
        Random,
        First,
    }

    internal enum AutoStartServerMergeMode
    {
        ReplaceAll,
        OverwriteByName,
        AddIfMissing,
    }

    internal enum StatusProjectionBandwidthUnit
    {
        Bytes,
        Bits,
    }

    internal sealed class RootLauncherConfiguration
    {
        public LoggingConfiguration Logging { get; set; } = new();
        public LauncherConfiguration Launcher { get; set; } = new();
    }

    internal sealed class LoggingConfiguration
    {
        public string Mode { get; set; } = "txt";
    }

    internal sealed class LauncherConfiguration
    {
        public int ListenPort { get; set; } = -1;
        public string? ServerPassword { get; set; }
        public string JoinServer { get; set; } = "none";
        public bool ColorfulConsoleStatus { get; set; } = true;
        public List<AutoStartServerConfiguration> AutoStartServers { get; set; } = [];
        public StatusProjectionConfiguration ConsoleStatus { get; set; } = new();
    }

    internal sealed class StatusProjectionConfiguration
    {
        public double TargetUps { get; set; } = StatusProjectionSettings.DefaultTargetUps;
        public double HealthyUpsDeviation { get; set; } = StatusProjectionSettings.DefaultHealthyUpsDeviation;
        public double WarningUpsDeviation { get; set; } = StatusProjectionSettings.DefaultWarningUpsDeviation;
        public double UtilHealthyMax { get; set; } = StatusProjectionSettings.DefaultUtilHealthyMax;
        public double UtilWarningMax { get; set; } = StatusProjectionSettings.DefaultUtilWarningMax;
        public int OnlineWarnRemainingSlots { get; set; } = StatusProjectionSettings.DefaultOnlineWarnRemainingSlots;
        public int OnlineBadRemainingSlots { get; set; } = StatusProjectionSettings.DefaultOnlineBadRemainingSlots;
        public string BandwidthUnit { get; set; } = "bytes";
        public double BandwidthRolloverThreshold { get; set; } = StatusProjectionSettings.DefaultBandwidthRolloverThreshold;
        [JsonPropertyName("upWarnKBps")]
        public double ServerUpWarnKBps { get; set; } = StatusProjectionSettings.DefaultServerUpWarnKBps;
        [JsonPropertyName("upBadKBps")]
        public double ServerUpBadKBps { get; set; } = StatusProjectionSettings.DefaultServerUpBadKBps;
        [JsonPropertyName("downWarnKBps")]
        public double ServerDownWarnKBps { get; set; } = StatusProjectionSettings.DefaultServerDownWarnKBps;
        [JsonPropertyName("downBadKBps")]
        public double ServerDownBadKBps { get; set; } = StatusProjectionSettings.DefaultServerDownBadKBps;
        [JsonPropertyName("launcherUpWarnKBps")]
        public double LauncherUpWarnKBps { get; set; } = StatusProjectionSettings.DefaultLauncherUpWarnKBps;
        [JsonPropertyName("launcherUpBadKBps")]
        public double LauncherUpBadKBps { get; set; } = StatusProjectionSettings.DefaultLauncherUpBadKBps;
        [JsonPropertyName("launcherDownWarnKBps")]
        public double LauncherDownWarnKBps { get; set; } = StatusProjectionSettings.DefaultLauncherDownWarnKBps;
        [JsonPropertyName("launcherDownBadKBps")]
        public double LauncherDownBadKBps { get; set; } = StatusProjectionSettings.DefaultLauncherDownBadKBps;
    }

    internal sealed record StatusProjectionBandwidthThresholds
    {
        public double UpWarnKBps { get; init; }
        public double UpBadKBps { get; init; }
        public double DownWarnKBps { get; init; }
        public double DownBadKBps { get; init; }
    }

    internal sealed record StatusProjectionSettings
    {
        public const double DefaultTargetUps = 60.0;
        public const double DefaultHealthyUpsDeviation = 2;
        public const double DefaultWarningUpsDeviation = 5.0;
        public const double DefaultUtilHealthyMax = 0.55;
        public const double DefaultUtilWarningMax = 0.80;
        public const int DefaultOnlineWarnRemainingSlots = 5;
        public const int DefaultOnlineBadRemainingSlots = 0;
        public const double DefaultBandwidthRolloverThreshold = 500.0;
        public const double DefaultServerUpWarnKBps = 800.0;
        public const double DefaultServerUpBadKBps = 1600.0;
        public const double DefaultServerDownWarnKBps = 50.0;
        public const double DefaultServerDownBadKBps = 100.0;
        public const double DefaultLauncherUpWarnKBps = 2400;
        public const double DefaultLauncherUpBadKBps = 4800;
        public const double DefaultLauncherDownWarnKBps = 150;
        public const double DefaultLauncherDownBadKBps = 300;
        public static StatusProjectionBandwidthThresholds DefaultServerBandwidth { get; } = new() {
            UpWarnKBps = DefaultServerUpWarnKBps,
            UpBadKBps = DefaultServerUpBadKBps,
            DownWarnKBps = DefaultServerDownWarnKBps,
            DownBadKBps = DefaultServerDownBadKBps,
        };

        public static StatusProjectionBandwidthThresholds DefaultLauncherBandwidth { get; } = new() {
            UpWarnKBps = DefaultLauncherUpWarnKBps,
            UpBadKBps = DefaultLauncherUpBadKBps,
            DownWarnKBps = DefaultLauncherDownWarnKBps,
            DownBadKBps = DefaultLauncherDownBadKBps,
        };
        public static StatusProjectionSettings Default { get; } = new();

        public double TargetUps { get; init; } = DefaultTargetUps;
        public double HealthyUpsDeviation { get; init; } = DefaultHealthyUpsDeviation;
        public double WarningUpsDeviation { get; init; } = DefaultWarningUpsDeviation;
        public double UtilHealthyMax { get; init; } = DefaultUtilHealthyMax;
        public double UtilWarningMax { get; init; } = DefaultUtilWarningMax;
        public int OnlineWarnRemainingSlots { get; init; } = DefaultOnlineWarnRemainingSlots;
        public int OnlineBadRemainingSlots { get; init; } = DefaultOnlineBadRemainingSlots;
        public StatusProjectionBandwidthUnit BandwidthUnit { get; init; } = StatusProjectionBandwidthUnit.Bytes;
        public double BandwidthRolloverThreshold { get; init; } = DefaultBandwidthRolloverThreshold;
        public StatusProjectionBandwidthThresholds ServerBandwidth { get; init; } = DefaultServerBandwidth;
        public StatusProjectionBandwidthThresholds LauncherBandwidth { get; init; } = DefaultLauncherBandwidth;

        public sealed class Builder
        {
            private Builder()
            {
                TargetUps = DefaultTargetUps;
                HealthyUpsDeviation = DefaultHealthyUpsDeviation;
                WarningUpsDeviation = DefaultWarningUpsDeviation;
                UtilHealthyMax = DefaultUtilHealthyMax;
                UtilWarningMax = DefaultUtilWarningMax;
                OnlineWarnRemainingSlots = DefaultOnlineWarnRemainingSlots;
                OnlineBadRemainingSlots = DefaultOnlineBadRemainingSlots;
                BandwidthUnit = StatusProjectionBandwidthUnit.Bytes;
                BandwidthRolloverThreshold = DefaultBandwidthRolloverThreshold;
                ServerBandwidth = DefaultServerBandwidth;
                LauncherBandwidth = DefaultLauncherBandwidth;
            }

            private Builder(StatusProjectionSettings source)
            {

                TargetUps = source.TargetUps;
                HealthyUpsDeviation = source.HealthyUpsDeviation;
                WarningUpsDeviation = source.WarningUpsDeviation;
                UtilHealthyMax = source.UtilHealthyMax;
                UtilWarningMax = source.UtilWarningMax;
                OnlineWarnRemainingSlots = source.OnlineWarnRemainingSlots;
                OnlineBadRemainingSlots = source.OnlineBadRemainingSlots;
                BandwidthUnit = source.BandwidthUnit;
                BandwidthRolloverThreshold = source.BandwidthRolloverThreshold;
                ServerBandwidth = source.ServerBandwidth;
                LauncherBandwidth = source.LauncherBandwidth;
            }

            public static Builder Create()
            {
                return new Builder();
            }

            public static Builder From(StatusProjectionSettings source)
            {
                return new Builder(source);
            }

            public double TargetUps { get; set; }
            public double HealthyUpsDeviation { get; set; }
            public double WarningUpsDeviation { get; set; }
            public double UtilHealthyMax { get; set; }
            public double UtilWarningMax { get; set; }
            public int OnlineWarnRemainingSlots { get; set; }
            public int OnlineBadRemainingSlots { get; set; }
            public StatusProjectionBandwidthUnit BandwidthUnit { get; set; }
            public double BandwidthRolloverThreshold { get; set; }
            public StatusProjectionBandwidthThresholds ServerBandwidth { get; set; }
            public StatusProjectionBandwidthThresholds LauncherBandwidth { get; set; }

            public StatusProjectionSettings Build()
            {
                return new StatusProjectionSettings {
                    TargetUps = TargetUps,
                    HealthyUpsDeviation = HealthyUpsDeviation,
                    WarningUpsDeviation = WarningUpsDeviation,
                    UtilHealthyMax = UtilHealthyMax,
                    UtilWarningMax = UtilWarningMax,
                    OnlineWarnRemainingSlots = OnlineWarnRemainingSlots,
                    OnlineBadRemainingSlots = OnlineBadRemainingSlots,
                    BandwidthUnit = BandwidthUnit,
                    BandwidthRolloverThreshold = BandwidthRolloverThreshold,
                    ServerBandwidth = ServerBandwidth,
                    LauncherBandwidth = LauncherBandwidth,
                };
            }
        }
    }

    internal sealed class AutoStartServerConfiguration
    {
        public string Name { get; set; } = "";
        public string WorldName { get; set; } = "";
        public string Seed { get; set; } = "";
        public string Difficulty { get; set; } = "master";
        public string Size { get; set; } = "large";
        public string Evil { get; set; } = "random";
    }

    internal sealed class LauncherCliOverrides
    {
        public int? ListenPort { get; set; }
        public string? ServerPassword { get; set; }
        public JoinServerMode? JoinServer { get; set; }
        public LogPersistenceMode? LogMode { get; set; }
        public bool? ColorfulConsoleStatus { get; set; }
        public AutoStartServerMergeMode? AutoStartServersMergeMode { get; set; }
        public bool HasAutoStartServers { get; set; }
        public List<AutoStartServerConfiguration> AutoStartServers { get; } = [];
    }

    internal sealed record LauncherRuntimeSettings
    {
        public LogPersistenceMode LogMode { get; init; } = LogPersistenceMode.Txt;
        public int ListenPort { get; init; } = -1;
        public string? ServerPassword { get; init; }
        public JoinServerMode JoinServer { get; init; } = JoinServerMode.None;
        public bool ColorfulConsoleStatus { get; init; } = true;
        public List<AutoStartServerConfiguration> AutoStartServers { get; init; } = [];
        public StatusProjectionSettings ConsoleStatus { get; init; } = StatusProjectionSettings.Default;

        public sealed class Builder
        {
            private Builder()
            {
                LogMode = LogPersistenceMode.Txt;
                ListenPort = -1;
                JoinServer = JoinServerMode.None;
                ColorfulConsoleStatus = true;
                AutoStartServers = [];
                ConsoleStatus = StatusProjectionSettings.Default;
            }

            private Builder(LauncherRuntimeSettings source)
            {

                LogMode = source.LogMode;
                ListenPort = source.ListenPort;
                ServerPassword = source.ServerPassword;
                JoinServer = source.JoinServer;
                ColorfulConsoleStatus = source.ColorfulConsoleStatus;
                AutoStartServers = [.. source.AutoStartServers];
                ConsoleStatus = source.ConsoleStatus;
            }

            public static Builder Create()
            {
                return new Builder();
            }

            public static Builder From(LauncherRuntimeSettings source)
            {
                return new Builder(source);
            }

            public LogPersistenceMode LogMode { get; set; }
            public int ListenPort { get; set; }
            public string? ServerPassword { get; set; }
            public JoinServerMode JoinServer { get; set; }
            public bool ColorfulConsoleStatus { get; set; }
            public List<AutoStartServerConfiguration> AutoStartServers { get; set; }
            public StatusProjectionSettings ConsoleStatus { get; set; }

            public LauncherRuntimeSettings Build()
            {
                return new LauncherRuntimeSettings {
                    LogMode = LogMode,
                    ListenPort = ListenPort,
                    ServerPassword = ServerPassword,
                    JoinServer = JoinServer,
                    ColorfulConsoleStatus = ColorfulConsoleStatus,
                    AutoStartServers = [.. AutoStartServers],
                    ConsoleStatus = ConsoleStatus,
                };
            }
        }
    }
}
