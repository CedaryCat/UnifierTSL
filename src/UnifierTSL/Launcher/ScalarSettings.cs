namespace UnifierTSL.Launcher
{
    internal static class ScalarSettings
    {
        public static ILauncherSettingSpec LogMode { get; } = new ScalarSettingSpec<string, LogPersistenceMode>(
            cliBindings: [
                LauncherCliBindings.CreateSingleValueBinding(
                    ["-logmode", "--log-mode"],
                    ParseLogModeCli,
                    static (overrides, value) => overrides.LogMode = value),
            ],
            readOverride: static overrides => GetNullableValue(overrides.LogMode),
            readConfig: static config => config.Logging?.Mode ?? "txt",
            writeConfig: static (config, value) => config.Logging.Mode = value,
            resolveRuntime: LauncherSettingValues.ResolveConfiguredLogMode,
            serializeConfig: LauncherSettingValues.DescribeLogMode,
            readRuntime: static settings => settings.LogMode,
            writeRuntime: static (builder, value) => builder.LogMode = value,
            applyReload: ApplyLogModeReload);

        public static ILauncherSettingSpec ListenPort { get; } = new ScalarSettingSpec<int, int>(
            cliBindings: [
                LauncherCliBindings.CreateSingleValueBinding(
                    ["-listen", "-port"],
                    ParseListenPortCli,
                    static (overrides, value) => overrides.ListenPort = value),
            ],
            readOverride: static overrides => GetNullableValue(overrides.ListenPort),
            readConfig: static config => config.Launcher?.ListenPort ?? -1,
            writeConfig: static (config, value) => config.Launcher.ListenPort = value,
            resolveRuntime: static value => value,
            serializeConfig: static value => value,
            readRuntime: static settings => settings.ListenPort,
            writeRuntime: static (builder, value) => builder.ListenPort = value,
            applyReload: ApplyListenPortReload,
            readInteractiveValue: static input => OptionalValue<int>.Some(input.ListenPort));

        public static ILauncherSettingSpec ServerPassword { get; } = new ScalarSettingSpec<string?, string?>(
            cliBindings: [
                LauncherCliBindings.CreateSingleValueBinding(
                    ["-password"],
                    static value => OptionalValue<string?>.Some(value),
                    static (overrides, value) => overrides.ServerPassword = value),
            ],
            readOverride: static overrides => overrides.ServerPassword is null
                ? OptionalValue<string?>.None
                : OptionalValue<string?>.Some(overrides.ServerPassword),
            readConfig: static config => config.Launcher?.ServerPassword,
            writeConfig: static (config, value) => config.Launcher.ServerPassword = value,
            resolveRuntime: static value => value,
            serializeConfig: static value => value,
            readRuntime: static settings => settings.ServerPassword,
            writeRuntime: static (builder, value) => builder.ServerPassword = value,
            applyReload: ApplyServerPasswordReload,
            readInteractiveValue: static input => OptionalValue<string?>.Some(input.ServerPassword));

        public static ILauncherSettingSpec JoinServer { get; } = new ScalarSettingSpec<string, JoinServerMode>(
            cliBindings: [
                LauncherCliBindings.CreateSingleValueBinding(
                    ["-joinserver"],
                    ParseJoinServerCli,
                    static (overrides, value) => overrides.JoinServer = value,
                    shouldApply: static state => !state.JoinServerConfigured,
                    onApplied: static state => state.JoinServerConfigured = true),
            ],
            readOverride: static overrides => GetNullableValue(overrides.JoinServer),
            readConfig: static config => config.Launcher?.JoinServer ?? "none",
            writeConfig: static (config, value) => config.Launcher.JoinServer = value,
            resolveRuntime: LauncherSettingValues.ResolveConfiguredJoinServerMode,
            serializeConfig: LauncherSettingValues.DescribeJoinServerMode,
            readRuntime: static settings => settings.JoinServer,
            writeRuntime: static (builder, value) => builder.JoinServer = value,
            applyReload: ApplyJoinServerReload,
            configEquals: static (left, right) => LauncherSettingValues.OrdinalIgnoreCaseEquals(left, right));

        public static ILauncherSettingSpec ColorfulConsoleStatus { get; } = new ScalarSettingSpec<bool, bool>(
            cliBindings: [
                LauncherCliBindings.CreateSingleValueBinding(
                    ["-colorful", "--colorful"],
                    ParseColorfulConsoleStatusCli,
                    static (overrides, value) => overrides.ColorfulConsoleStatus = value),
                LauncherCliBindings.CreateFlagBinding(
                    ["--no-colorful"],
                    false,
                    static (overrides, value) => overrides.ColorfulConsoleStatus = value),
            ],
            readOverride: static overrides => GetNullableValue(overrides.ColorfulConsoleStatus),
            readConfig: static config => config.Launcher?.ColorfulConsoleStatus ?? true,
            writeConfig: static (config, value) => config.Launcher.ColorfulConsoleStatus = value,
            resolveRuntime: static value => value,
            serializeConfig: static value => value,
            readRuntime: static settings => settings.ColorfulConsoleStatus,
            writeRuntime: static (builder, value) => builder.ColorfulConsoleStatus = value,
            applyReload: ApplyColorfulConsoleStatusReload);

        private static OptionalValue<int> ParseListenPortCli(string value) {
            if (int.TryParse(value, out int port)) {
                return OptionalValue<int>.Some(port);
            }

            UnifierApi.Logger.Warning(
                GetParticularString("{0} is user input value for port number", $"Invalid port number specified: {value}"),
                category: LauncherCategories.Launcher);
            return OptionalValue<int>.None;
        }

        private static OptionalValue<JoinServerMode> ParseJoinServerCli(string value) {
            if (LauncherSettingValues.TryParseJoinServerMode(value, out JoinServerMode joinMode)) {
                return OptionalValue<JoinServerMode>.Some(joinMode);
            }

            UnifierApi.Logger.Warning(
                GetParticularString("{0} is user input value for join server mode", $"Invalid join server mode: {value}"),
                category: LauncherCategories.Launcher);
            return OptionalValue<JoinServerMode>.None;
        }

        private static OptionalValue<LogPersistenceMode> ParseLogModeCli(string value) {
            if (LauncherSettingValues.TryParseLogMode(value, out LogPersistenceMode mode)) {
                return OptionalValue<LogPersistenceMode>.Some(mode);
            }

            UnifierApi.Logger.Warning(
                GetParticularString("{0} is user input value for log mode", $"Invalid log mode specified: {value}"),
                category: LauncherCategories.Logging);
            return OptionalValue<LogPersistenceMode>.None;
        }

        private static OptionalValue<bool> ParseColorfulConsoleStatusCli(string value) {
            if (LauncherSettingValues.TryParseColorfulConsoleStatus(value, emptyValue: true, out bool colorfulConsoleStatus)) {
                return OptionalValue<bool>.Some(colorfulConsoleStatus);
            }

            LauncherSettingValues.WarnInvalidColorfulConsoleStatus(value, LauncherCategories.Launcher);
            return OptionalValue<bool>.None;
        }

        private static void ApplyLogModeReload(
            LauncherRuntimeSettings.Builder builder,
            LogPersistenceMode current,
            LogPersistenceMode desired,
            ReloadContext _) {

            if (current != desired) {
                UnifierApi.Logger.Warning(
                    GetParticularString("{0} is current logging mode, {1} is desired logging mode", $"logging.mode changed from '{LauncherSettingValues.DescribeLogMode(current)}' to '{LauncherSettingValues.DescribeLogMode(desired)}'. Restart is required before this setting takes effect."),
                    category: LauncherCategories.Config);
            }

            builder.LogMode = desired;
        }

        private static void ApplyListenPortReload(
            LauncherRuntimeSettings.Builder builder,
            int current,
            int desired,
            ReloadContext context) {

            if (current == desired) {
                builder.ListenPort = desired;
                return;
            }

            if (!LauncherPortRules.IsValidListenPort(desired)) {
                UnifierApi.Logger.Warning(
                    GetParticularString("{0} is current listen port, {1} is desired listen port, {2} is active listen port", $"launcher.listenPort changed from '{LauncherSettingValues.DescribeListenPort(current)}' to '{LauncherSettingValues.DescribeListenPort(desired)}', but the new value is invalid. Keeping port {UnifiedServerCoordinator.ListenPort}."),
                    category: LauncherCategories.Config);
                return;
            }

            if (UnifiedServerCoordinator.RebindListener(desired)) {
                context.ApplyListenPort(desired);
                UnifierApi.Logger.Info(
                    GetParticularString("{0} is current listen port, {1} is desired listen port", $"launcher.listenPort changed from '{LauncherSettingValues.DescribeListenPort(current)}' to '{LauncherSettingValues.DescribeListenPort(desired)}'. The active listener has been rebound."),
                    category: LauncherCategories.Config);
                builder.ListenPort = desired;
                return;
            }

            UnifierApi.Logger.Warning(
                GetParticularString("{0} is current listen port, {1} is desired listen port, {2} is active listen port", $"launcher.listenPort changed from '{LauncherSettingValues.DescribeListenPort(current)}' to '{LauncherSettingValues.DescribeListenPort(desired)}', but rebinding failed. Keeping port {UnifiedServerCoordinator.ListenPort}."),
                category: LauncherCategories.Config);
        }

        private static void ApplyServerPasswordReload(
            LauncherRuntimeSettings.Builder builder,
            string? current,
            string? desired,
            ReloadContext context) {

            string currentPassword = current ?? "";
            string desiredPassword = desired ?? "";
            if (!LauncherSettingValues.OrdinalEquals(currentPassword, desiredPassword)) {
                context.ApplyServerPassword(desiredPassword);
                UnifierApi.Logger.Info(
                    GetString("launcher.serverPassword changed. New connections will use the updated password policy."),
                    category: LauncherCategories.Config);
            }

            builder.ServerPassword = desiredPassword;
        }

        private static void ApplyJoinServerReload(
            LauncherRuntimeSettings.Builder builder,
            JoinServerMode current,
            JoinServerMode desired,
            ReloadContext _) {

            if (current != desired) {
                UnifierApi.Logger.Info(
                    GetParticularString("{0} is current join server mode, {1} is desired join server mode", $"launcher.joinServer changed from '{LauncherSettingValues.DescribeJoinServerMode(current)}' to '{LauncherSettingValues.DescribeJoinServerMode(desired)}'. Future joins will use the updated policy."),
                    category: LauncherCategories.Config);
            }

            builder.JoinServer = desired;
        }

        private static void ApplyColorfulConsoleStatusReload(
            LauncherRuntimeSettings.Builder builder,
            bool current,
            bool desired,
            ReloadContext _) {

            if (current != desired) {
                UnifierApi.Logger.Info(
                    GetParticularString("{0} is current colorfulConsoleStatus value, {1} is desired colorfulConsoleStatus value",
                        $"launcher.colorfulConsoleStatus changed from '{LauncherSettingValues.DescribeColorfulConsoleStatus(current)}' to '{LauncherSettingValues.DescribeColorfulConsoleStatus(desired)}'. Status bar appearance has been refreshed."),
                    category: LauncherCategories.Config);
            }

            builder.ColorfulConsoleStatus = desired;
        }

        private static OptionalValue<T> GetNullableValue<T>(T? value)
            where T : struct {

            return value.HasValue
                ? OptionalValue<T>.Some(value.Value)
                : OptionalValue<T>.None;
        }
    }
}
