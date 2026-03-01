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
        public List<AutoStartServerConfiguration> AutoStartServers { get; set; } = [];
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
        public List<AutoStartServerConfiguration> AutoStartServers { get; init; } = [];
    }
}
