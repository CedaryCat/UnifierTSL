using System.Globalization;
using Terraria.GameContent.UI.States;

namespace UnifierTSL.Launcher
{
    internal static class LauncherSettingValues
    {
        public static string NormalizeWorldName(string? value) {
            return value?.Trim() ?? "";
        }

        public static string NormalizeAutoStartServerName(string? value) {
            return value?.Trim() ?? "";
        }

        public static string TrimOrEmpty(string? value) {
            return value?.Trim() ?? "";
        }

        public static bool OrdinalEquals(string? left, string? right) {
            return string.Equals(left, right, StringComparison.Ordinal);
        }

        public static bool OrdinalIgnoreCaseEquals(string? left, string? right) {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        public static string DescribeJoinServerMode(JoinServerMode mode) {
            return mode switch {
                JoinServerMode.Random => "random",
                JoinServerMode.First => "first",
                _ => "none",
            };
        }

        public static string DescribeLogMode(LogPersistenceMode mode) {
            return mode switch {
                LogPersistenceMode.None => "none",
                LogPersistenceMode.Sqlite => "sqlite",
                _ => "txt",
            };
        }

        public static string DescribeAutoStartServerMergeMode(AutoStartServerMergeMode mode) {
            return mode switch {
                AutoStartServerMergeMode.OverwriteByName => "overwrite",
                AutoStartServerMergeMode.AddIfMissing => "append",
                _ => "replace",
            };
        }

        public static string DescribeListenPort(int port) {
            return LauncherPortRules.IsValidListenPort(port)
                ? port.ToString(CultureInfo.InvariantCulture)
                : "unset";
        }

        public static string DescribeColorfulConsoleStatus(bool enabled) {
            return enabled ? "true" : "false";
        }

        public static string DescribeStatusProjectionBandwidthUnit(StatusProjectionBandwidthUnit unit) {
            return unit switch {
                StatusProjectionBandwidthUnit.Bits => "bits",
                _ => "bytes",
            };
        }

        public static bool TryResolveDifficulty(string? value, out int difficulty) {
            string text = value?.Trim() ?? "";
            if (int.TryParse(text, out difficulty) && difficulty >= 0 && difficulty <= 3) {
                return true;
            }

            if (OrdinalIgnoreCaseEquals(text, nameof(UIWorldCreation.WorldDifficultyId.Normal)) || text == "n") {
                difficulty = 0;
                return true;
            }

            if (OrdinalIgnoreCaseEquals(text, nameof(UIWorldCreation.WorldDifficultyId.Expert)) || text == "e") {
                difficulty = 1;
                return true;
            }

            if (OrdinalIgnoreCaseEquals(text, nameof(UIWorldCreation.WorldDifficultyId.Master)) || text == "m") {
                difficulty = 2;
                return true;
            }

            if (OrdinalIgnoreCaseEquals(text, nameof(UIWorldCreation.WorldDifficultyId.Creative)) || text == "c") {
                difficulty = 3;
                return true;
            }

            difficulty = 0;
            return false;
        }

        public static bool TryResolveSize(string? value, out int size) {
            string text = value?.Trim() ?? "";
            if (int.TryParse(text, out size) && size >= 1 && size <= 3) {
                return true;
            }

            if (OrdinalIgnoreCaseEquals(text, nameof(UIWorldCreation.WorldSizeId.Small)) || text == "s") {
                size = 1;
                return true;
            }

            if (OrdinalIgnoreCaseEquals(text, nameof(UIWorldCreation.WorldSizeId.Medium)) || text == "m") {
                size = 2;
                return true;
            }

            if (OrdinalIgnoreCaseEquals(text, nameof(UIWorldCreation.WorldSizeId.Large)) || text == "l") {
                size = 3;
                return true;
            }

            size = 0;
            return false;
        }

        public static bool TryResolveEvil(string? value, out int evil) {
            string text = value?.Trim() ?? "";
            if (int.TryParse(text, out evil) && evil >= 0 && evil <= 2) {
                return true;
            }

            if (OrdinalIgnoreCaseEquals(text, nameof(UIWorldCreation.WorldEvilId.Random))) {
                evil = 0;
                return true;
            }

            if (OrdinalIgnoreCaseEquals(text, nameof(UIWorldCreation.WorldEvilId.Corruption))) {
                evil = 1;
                return true;
            }

            if (OrdinalIgnoreCaseEquals(text, nameof(UIWorldCreation.WorldEvilId.Crimson))) {
                evil = 2;
                return true;
            }

            evil = 0;
            return false;
        }

        public static bool TryParseAutoStartServerMergeMode(string? value, out AutoStartServerMergeMode mode) {
            string text = value?.Trim() ?? "";
            if (OrdinalIgnoreCaseEquals(text, "replace") || OrdinalIgnoreCaseEquals(text, "clean")) {
                mode = AutoStartServerMergeMode.ReplaceAll;
                return true;
            }

            if (OrdinalIgnoreCaseEquals(text, "overwrite") || OrdinalIgnoreCaseEquals(text, "name")) {
                mode = AutoStartServerMergeMode.OverwriteByName;
                return true;
            }

            if (OrdinalIgnoreCaseEquals(text, "append") || OrdinalIgnoreCaseEquals(text, "add")) {
                mode = AutoStartServerMergeMode.AddIfMissing;
                return true;
            }

            mode = AutoStartServerMergeMode.ReplaceAll;
            return false;
        }

        public static bool TryParseJoinServerMode(string? value, out JoinServerMode joinMode) {
            string text = value?.Trim() ?? "";
            if (text.Length == 0 || OrdinalIgnoreCaseEquals(text, "none")) {
                joinMode = JoinServerMode.None;
                return true;
            }

            if (text is "random" or "rnd" or "r") {
                joinMode = JoinServerMode.Random;
                return true;
            }

            if (text is "first" or "f") {
                joinMode = JoinServerMode.First;
                return true;
            }

            joinMode = JoinServerMode.None;
            return false;
        }

        public static JoinServerMode ResolveConfiguredJoinServerMode(string? value) {
            if (TryParseJoinServerMode(value, out JoinServerMode mode)) {
                return mode;
            }

            UnifierApi.Logger.Warning(
                GetParticularString("{0} is configured joinServer value", $"Invalid joinServer setting '{value}'. Falling back to 'none'."),
                category: LauncherCategories.Config);
            return JoinServerMode.None;
        }

        public static bool TryParseLogMode(string? value, out LogPersistenceMode mode) {
            string text = value?.Trim() ?? "";
            if (OrdinalIgnoreCaseEquals(text, "txt")) {
                mode = LogPersistenceMode.Txt;
                return true;
            }

            if (OrdinalIgnoreCaseEquals(text, "none")) {
                mode = LogPersistenceMode.None;
                return true;
            }

            if (OrdinalIgnoreCaseEquals(text, "sqlite")) {
                mode = LogPersistenceMode.Sqlite;
                return true;
            }

            mode = LogPersistenceMode.Txt;
            return false;
        }

        public static LogPersistenceMode ResolveConfiguredLogMode(string? value) {
            if (TryParseLogMode(value, out LogPersistenceMode mode)) {
                return mode;
            }

            UnifierApi.Logger.Warning(
                GetParticularString("{0} is configured logging.mode value", $"Invalid logging.mode setting '{value}'. Falling back to 'txt'."),
                category: LauncherCategories.Config);
            return LogPersistenceMode.Txt;
        }

        public static bool TryParseColorfulConsoleStatus(string? value, bool emptyValue, out bool enabled) {
            string text = value?.Trim() ?? string.Empty;
            if (text.Length == 0) {
                enabled = emptyValue;
                return true;
            }

            if (text is "1" or "+") {
                enabled = true;
                return true;
            }

            if (text is "0" or "-") {
                enabled = false;
                return true;
            }

            if (bool.TryParse(text, out enabled)) {
                return true;
            }

            if (OrdinalIgnoreCaseEquals(text, "on") || OrdinalIgnoreCaseEquals(text, "enable") || OrdinalIgnoreCaseEquals(text, "enabled")) {
                enabled = true;
                return true;
            }

            if (OrdinalIgnoreCaseEquals(text, "off") || OrdinalIgnoreCaseEquals(text, "disable") || OrdinalIgnoreCaseEquals(text, "disabled")) {
                enabled = false;
                return true;
            }

            enabled = true;
            return false;
        }

        public static void WarnInvalidColorfulConsoleStatus(string? value, string category) {
            UnifierApi.Logger.Warning(
                GetParticularString("{0} is user input value for colorful console status", $"Invalid colorful console status value: {value}"),
                category: category);
            UnifierApi.Logger.Warning(
                GetString("Expected value: true/false, on/off, 1/0, or use '--no-colorful' to disable."),
                category: category);
        }

        public static bool TryParseStatusProjectionBandwidthUnit(string? value, out StatusProjectionBandwidthUnit unit) {
            string text = value?.Trim() ?? "";
            if (text.Length == 0 || OrdinalIgnoreCaseEquals(text, "bytes") || OrdinalIgnoreCaseEquals(text, "byte")) {
                unit = StatusProjectionBandwidthUnit.Bytes;
                return true;
            }

            if (OrdinalIgnoreCaseEquals(text, "bits") || OrdinalIgnoreCaseEquals(text, "bit")) {
                unit = StatusProjectionBandwidthUnit.Bits;
                return true;
            }

            unit = StatusProjectionBandwidthUnit.Bytes;
            return false;
        }

        public static StatusProjectionBandwidthUnit ResolveConfiguredStatusProjectionBandwidthUnit(string? value) {
            if (TryParseStatusProjectionBandwidthUnit(value, out StatusProjectionBandwidthUnit unit)) {
                return unit;
            }

            UnifierApi.Logger.Warning(
                GetParticularString("{0} is configured launcher.consoleStatus.bandwidthUnit value", $"Invalid launcher.consoleStatus.bandwidthUnit setting '{value}'. Falling back to 'bytes'."),
                category: LauncherCategories.Config);
            return StatusProjectionBandwidthUnit.Bytes;
        }

        public static double ResolveConfiguredStatusProjectionBandwidthRolloverThreshold(double value) {
            if (!double.IsFinite(value) || value <= 0d) {
                UnifierApi.Logger.Warning(
                    GetParticularString("{0} is configured launcher.consoleStatus.bandwidthRolloverThreshold value", $"Invalid launcher.consoleStatus.bandwidthRolloverThreshold setting '{value}'. Falling back to '{StatusProjectionSettings.DefaultBandwidthRolloverThreshold}'."),
                    category: LauncherCategories.Config);
                return StatusProjectionSettings.DefaultBandwidthRolloverThreshold;
            }

            return value;
        }

    }
}
