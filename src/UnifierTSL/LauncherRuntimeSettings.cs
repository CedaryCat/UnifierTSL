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
        public ConsoleStatusThresholdsConfiguration ConsoleStatusThresholds { get; set; } = new();
    }

    internal sealed class ConsoleStatusThresholdsConfiguration
    {
        public double TargetUps { get; set; } = ConsoleStatusThresholds.DefaultTargetUps;
        public double HealthyUpsDeviation { get; set; } = ConsoleStatusThresholds.DefaultHealthyUpsDeviation;
        public double WarningUpsDeviation { get; set; } = ConsoleStatusThresholds.DefaultWarningUpsDeviation;
        public double UtilHealthyMax { get; set; } = ConsoleStatusThresholds.DefaultUtilHealthyMax;
        public double UtilWarningMax { get; set; } = ConsoleStatusThresholds.DefaultUtilWarningMax;
        public int OnlineWarnRemainingSlots { get; set; } = ConsoleStatusThresholds.DefaultOnlineWarnRemainingSlots;
        public int OnlineBadRemainingSlots { get; set; } = ConsoleStatusThresholds.DefaultOnlineBadRemainingSlots;
        public double UpWarnKbps { get; set; } = ConsoleStatusThresholds.DefaultUpWarnKbps;
        public double UpBadKbps { get; set; } = ConsoleStatusThresholds.DefaultUpBadKbps;
        public double DownWarnKbps { get; set; } = ConsoleStatusThresholds.DefaultDownWarnKbps;
        public double DownBadKbps { get; set; } = ConsoleStatusThresholds.DefaultDownBadKbps;
    }

    internal sealed record ConsoleStatusThresholds
    {
        public const double DefaultTargetUps = 60.0;
        public const double DefaultHealthyUpsDeviation = 1.2;
        public const double DefaultWarningUpsDeviation = 4.0;
        public const double DefaultUtilHealthyMax = 0.50;
        public const double DefaultUtilWarningMax = 0.80;
        public const int DefaultOnlineWarnRemainingSlots = 5;
        public const int DefaultOnlineBadRemainingSlots = 0;
        public const double DefaultUpWarnKbps = 65.0;
        public const double DefaultUpBadKbps = 75.0;
        public const double DefaultDownWarnKbps = 85.0;
        public const double DefaultDownBadKbps = 95.0;

        public static ConsoleStatusThresholds Default { get; } = new();

        public double TargetUps { get; init; } = DefaultTargetUps;
        public double HealthyUpsDeviation { get; init; } = DefaultHealthyUpsDeviation;
        public double WarningUpsDeviation { get; init; } = DefaultWarningUpsDeviation;
        public double UtilHealthyMax { get; init; } = DefaultUtilHealthyMax;
        public double UtilWarningMax { get; init; } = DefaultUtilWarningMax;
        public int OnlineWarnRemainingSlots { get; init; } = DefaultOnlineWarnRemainingSlots;
        public int OnlineBadRemainingSlots { get; init; } = DefaultOnlineBadRemainingSlots;
        public double UpWarnKbps { get; init; } = DefaultUpWarnKbps;
        public double UpBadKbps { get; init; } = DefaultUpBadKbps;
        public double DownWarnKbps { get; init; } = DefaultDownWarnKbps;
        public double DownBadKbps { get; init; } = DefaultDownBadKbps;
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

    internal sealed class LauncherRuntimeSettings
    {
        public LogPersistenceMode LogMode { get; init; } = LogPersistenceMode.Txt;
        public int ListenPort { get; init; } = -1;
        public string? ServerPassword { get; init; }
        public JoinServerMode JoinServer { get; init; } = JoinServerMode.None;
        public bool ColorfulConsoleStatus { get; init; } = true;
        public List<AutoStartServerConfiguration> AutoStartServers { get; init; } = [];
        public ConsoleStatusThresholds ConsoleStatusThresholds { get; init; } = ConsoleStatusThresholds.Default;
    }
}
