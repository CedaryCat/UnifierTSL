using System.Text.Json.Serialization;

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

    internal enum ConsoleStatusBandwidthUnit
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
        public ConsoleStatusConfiguration ConsoleStatus { get; set; } = new();
    }

    internal sealed class ConsoleStatusConfiguration
    {
        public double TargetUps { get; set; } = ConsoleStatusSettings.DefaultTargetUps;
        public double HealthyUpsDeviation { get; set; } = ConsoleStatusSettings.DefaultHealthyUpsDeviation;
        public double WarningUpsDeviation { get; set; } = ConsoleStatusSettings.DefaultWarningUpsDeviation;
        public double UtilHealthyMax { get; set; } = ConsoleStatusSettings.DefaultUtilHealthyMax;
        public double UtilWarningMax { get; set; } = ConsoleStatusSettings.DefaultUtilWarningMax;
        public int OnlineWarnRemainingSlots { get; set; } = ConsoleStatusSettings.DefaultOnlineWarnRemainingSlots;
        public int OnlineBadRemainingSlots { get; set; } = ConsoleStatusSettings.DefaultOnlineBadRemainingSlots;
        public string BandwidthUnit { get; set; } = "bytes";
        public double BandwidthRolloverThreshold { get; set; } = ConsoleStatusSettings.DefaultBandwidthRolloverThreshold;
        [JsonPropertyName("upWarnKBps")]
        public double ServerUpWarnKBps { get; set; } = ConsoleStatusSettings.DefaultServerUpWarnKBps;
        [JsonPropertyName("upBadKBps")]
        public double ServerUpBadKBps { get; set; } = ConsoleStatusSettings.DefaultServerUpBadKBps;
        [JsonPropertyName("downWarnKBps")]
        public double ServerDownWarnKBps { get; set; } = ConsoleStatusSettings.DefaultServerDownWarnKBps;
        [JsonPropertyName("downBadKBps")]
        public double ServerDownBadKBps { get; set; } = ConsoleStatusSettings.DefaultServerDownBadKBps;
        [JsonPropertyName("launcherUpWarnKBps")]
        public double LauncherUpWarnKBps { get; set; } = ConsoleStatusSettings.DefaultLauncherUpWarnKBps;
        [JsonPropertyName("launcherUpBadKBps")]
        public double LauncherUpBadKBps { get; set; } = ConsoleStatusSettings.DefaultLauncherUpBadKBps;
        [JsonPropertyName("launcherDownWarnKBps")]
        public double LauncherDownWarnKBps { get; set; } = ConsoleStatusSettings.DefaultLauncherDownWarnKBps;
        [JsonPropertyName("launcherDownBadKBps")]
        public double LauncherDownBadKBps { get; set; } = ConsoleStatusSettings.DefaultLauncherDownBadKBps;
    }

    internal sealed record ConsoleStatusBandwidthThresholds
    {
        public double UpWarnKBps { get; init; }
        public double UpBadKBps { get; init; }
        public double DownWarnKBps { get; init; }
        public double DownBadKBps { get; init; }
    }

    internal sealed record ConsoleStatusSettings
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

        public static ConsoleStatusBandwidthThresholds DefaultServerBandwidth { get; } = new() {
            UpWarnKBps = DefaultServerUpWarnKBps,
            UpBadKBps = DefaultServerUpBadKBps,
            DownWarnKBps = DefaultServerDownWarnKBps,
            DownBadKBps = DefaultServerDownBadKBps,
        };

        public static ConsoleStatusBandwidthThresholds DefaultLauncherBandwidth { get; } = new() {
            UpWarnKBps = DefaultLauncherUpWarnKBps,
            UpBadKBps = DefaultLauncherUpBadKBps,
            DownWarnKBps = DefaultLauncherDownWarnKBps,
            DownBadKBps = DefaultLauncherDownBadKBps,
        };
        public static ConsoleStatusSettings Default { get; } = new();

        public double TargetUps { get; init; } = DefaultTargetUps;
        public double HealthyUpsDeviation { get; init; } = DefaultHealthyUpsDeviation;
        public double WarningUpsDeviation { get; init; } = DefaultWarningUpsDeviation;
        public double UtilHealthyMax { get; init; } = DefaultUtilHealthyMax;
        public double UtilWarningMax { get; init; } = DefaultUtilWarningMax;
        public int OnlineWarnRemainingSlots { get; init; } = DefaultOnlineWarnRemainingSlots;
        public int OnlineBadRemainingSlots { get; init; } = DefaultOnlineBadRemainingSlots;
        public ConsoleStatusBandwidthUnit BandwidthUnit { get; init; } = ConsoleStatusBandwidthUnit.Bytes;
        public double BandwidthRolloverThreshold { get; init; } = DefaultBandwidthRolloverThreshold;
        public ConsoleStatusBandwidthThresholds ServerBandwidth { get; init; } = DefaultServerBandwidth;
        public ConsoleStatusBandwidthThresholds LauncherBandwidth { get; init; } = DefaultLauncherBandwidth;
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
        public ConsoleStatusSettings ConsoleStatus { get; init; } = ConsoleStatusSettings.Default;
    }
}
