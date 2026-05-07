namespace UnifierTSL.Launcher
{
    internal static class StatusProjectionSetting
    {
        private static readonly IReadOnlyList<IStatusProjectionFieldSpec> fields = [
            new FieldSpec<double, double>(
                readConfig: static config => config.TargetUps,
                writeConfig: static (config, value) => config.TargetUps = value,
                resolveRuntime: static value => value,
                serializeConfig: static value => value,
                readRuntime: static settings => settings.TargetUps,
                writeRuntime: static (builder, value) => builder.TargetUps = value),
            new FieldSpec<double, double>(
                readConfig: static config => config.HealthyUpsDeviation,
                writeConfig: static (config, value) => config.HealthyUpsDeviation = value,
                resolveRuntime: static value => value,
                serializeConfig: static value => value,
                readRuntime: static settings => settings.HealthyUpsDeviation,
                writeRuntime: static (builder, value) => builder.HealthyUpsDeviation = value),
            new FieldSpec<double, double>(
                readConfig: static config => config.WarningUpsDeviation,
                writeConfig: static (config, value) => config.WarningUpsDeviation = value,
                resolveRuntime: static value => value,
                serializeConfig: static value => value,
                readRuntime: static settings => settings.WarningUpsDeviation,
                writeRuntime: static (builder, value) => builder.WarningUpsDeviation = value),
            new FieldSpec<double, double>(
                readConfig: static config => config.UtilHealthyMax,
                writeConfig: static (config, value) => config.UtilHealthyMax = value,
                resolveRuntime: static value => value,
                serializeConfig: static value => value,
                readRuntime: static settings => settings.UtilHealthyMax,
                writeRuntime: static (builder, value) => builder.UtilHealthyMax = value),
            new FieldSpec<double, double>(
                readConfig: static config => config.UtilWarningMax,
                writeConfig: static (config, value) => config.UtilWarningMax = value,
                resolveRuntime: static value => value,
                serializeConfig: static value => value,
                readRuntime: static settings => settings.UtilWarningMax,
                writeRuntime: static (builder, value) => builder.UtilWarningMax = value),
            new FieldSpec<int, int>(
                readConfig: static config => config.OnlineWarnRemainingSlots,
                writeConfig: static (config, value) => config.OnlineWarnRemainingSlots = value,
                resolveRuntime: static value => value,
                serializeConfig: static value => value,
                readRuntime: static settings => settings.OnlineWarnRemainingSlots,
                writeRuntime: static (builder, value) => builder.OnlineWarnRemainingSlots = value),
            new FieldSpec<int, int>(
                readConfig: static config => config.OnlineBadRemainingSlots,
                writeConfig: static (config, value) => config.OnlineBadRemainingSlots = value,
                resolveRuntime: static value => value,
                serializeConfig: static value => value,
                readRuntime: static settings => settings.OnlineBadRemainingSlots,
                writeRuntime: static (builder, value) => builder.OnlineBadRemainingSlots = value),
            new FieldSpec<string, StatusProjectionBandwidthUnit>(
                readConfig: static config => config.BandwidthUnit ?? "bytes",
                writeConfig: static (config, value) => config.BandwidthUnit = value,
                resolveRuntime: LauncherSettingValues.ResolveConfiguredStatusProjectionBandwidthUnit,
                serializeConfig: LauncherSettingValues.DescribeStatusProjectionBandwidthUnit,
                readRuntime: static settings => settings.BandwidthUnit,
                writeRuntime: static (builder, value) => builder.BandwidthUnit = value,
                configEquals: static (left, right) => LauncherSettingValues.OrdinalIgnoreCaseEquals(left, right)),
            new FieldSpec<double, double>(
                readConfig: static config => config.BandwidthRolloverThreshold,
                writeConfig: static (config, value) => config.BandwidthRolloverThreshold = value,
                resolveRuntime: LauncherSettingValues.ResolveConfiguredStatusProjectionBandwidthRolloverThreshold,
                serializeConfig: static value => value,
                readRuntime: static settings => settings.BandwidthRolloverThreshold,
                writeRuntime: static (builder, value) => builder.BandwidthRolloverThreshold = value),
            new FieldSpec<StatusProjectionBandwidthThresholds, StatusProjectionBandwidthThresholds>(
                readConfig: ReadServerBandwidth,
                writeConfig: WriteServerBandwidth,
                resolveRuntime: static value => value,
                serializeConfig: static value => value,
                readRuntime: static settings => settings.ServerBandwidth,
                writeRuntime: static (builder, value) => builder.ServerBandwidth = value),
            new FieldSpec<StatusProjectionBandwidthThresholds, StatusProjectionBandwidthThresholds>(
                readConfig: ReadLauncherBandwidth,
                writeConfig: WriteLauncherBandwidth,
                resolveRuntime: static value => value,
                serializeConfig: static value => value,
                readRuntime: static settings => settings.LauncherBandwidth,
                writeRuntime: static (builder, value) => builder.LauncherBandwidth = value),
        ];

        public static ILauncherSettingSpec Spec { get; } = new ScalarSettingSpec<StatusProjectionConfiguration, StatusProjectionSettings>(
            cliBindings: [],
            readOverride: static _ => OptionalValue<StatusProjectionSettings>.None,
            readConfig: static config => config.Launcher?.ConsoleStatus ?? new StatusProjectionConfiguration(),
            writeConfig: static (config, value) => config.Launcher.ConsoleStatus = CloneConfiguration(value),
            resolveRuntime: Resolve,
            serializeConfig: Serialize,
            readRuntime: static settings => settings.ConsoleStatus,
            writeRuntime: static (builder, value) => builder.ConsoleStatus = value,
            applyReload: ApplyReload,
            cloneConfig: CloneConfiguration,
            configEquals: Equivalent);

        public static StatusProjectionConfiguration CloneConfiguration(StatusProjectionConfiguration? source) {
            StatusProjectionConfiguration clone = new();
            StatusProjectionConfiguration value = source ?? new StatusProjectionConfiguration();
            foreach (IStatusProjectionFieldSpec field in fields) {
                field.CopyConfig(value, clone);
            }

            return clone;
        }

        public static bool Equivalent(
            StatusProjectionConfiguration? left,
            StatusProjectionConfiguration? right) {

            StatusProjectionConfiguration leftValue = left ?? new StatusProjectionConfiguration();
            StatusProjectionConfiguration rightValue = right ?? new StatusProjectionConfiguration();
            foreach (IStatusProjectionFieldSpec field in fields) {
                if (!field.ConfigEquivalent(leftValue, rightValue)) {
                    return false;
                }
            }

            return true;
        }

        public static StatusProjectionSettings Resolve(StatusProjectionConfiguration? configured) {
            StatusProjectionConfiguration source = configured ?? new StatusProjectionConfiguration();
            var builder = StatusProjectionSettings.Builder.Create();
            foreach (IStatusProjectionFieldSpec field in fields) {
                field.ApplyConfiguredValue(source, builder);
            }

            return builder.Build();
        }

        public static StatusProjectionConfiguration Serialize(StatusProjectionSettings source) {
            StatusProjectionConfiguration config = new();
            foreach (IStatusProjectionFieldSpec field in fields) {
                field.CopyRuntime(source, config);
            }

            return config;
        }

        private static void ApplyReload(
            LauncherRuntimeSettings.Builder builder,
            StatusProjectionSettings current,
            StatusProjectionSettings desired,
            ReloadContext _) {

            if (current != desired) {
                UnifierApi.Logger.Info(
                    GetString("launcher.consoleStatus changed. Command-line status settings are now using the reloaded values."),
                    category: LauncherCategories.Config);
            }

            builder.ConsoleStatus = desired;
        }

        private static StatusProjectionBandwidthThresholds ReadServerBandwidth(StatusProjectionConfiguration config) {
            return new StatusProjectionBandwidthThresholds {
                UpWarnKBps = config.ServerUpWarnKBps,
                UpBadKBps = config.ServerUpBadKBps,
                DownWarnKBps = config.ServerDownWarnKBps,
                DownBadKBps = config.ServerDownBadKBps,
            };
        }

        private static void WriteServerBandwidth(
            StatusProjectionConfiguration config,
            StatusProjectionBandwidthThresholds value) {

            config.ServerUpWarnKBps = value.UpWarnKBps;
            config.ServerUpBadKBps = value.UpBadKBps;
            config.ServerDownWarnKBps = value.DownWarnKBps;
            config.ServerDownBadKBps = value.DownBadKBps;
        }

        private static StatusProjectionBandwidthThresholds ReadLauncherBandwidth(StatusProjectionConfiguration config) {
            return new StatusProjectionBandwidthThresholds {
                UpWarnKBps = config.LauncherUpWarnKBps,
                UpBadKBps = config.LauncherUpBadKBps,
                DownWarnKBps = config.LauncherDownWarnKBps,
                DownBadKBps = config.LauncherDownBadKBps,
            };
        }

        private static void WriteLauncherBandwidth(
            StatusProjectionConfiguration config,
            StatusProjectionBandwidthThresholds value) {

            config.LauncherUpWarnKBps = value.UpWarnKBps;
            config.LauncherUpBadKBps = value.UpBadKBps;
            config.LauncherDownWarnKBps = value.DownWarnKBps;
            config.LauncherDownBadKBps = value.DownBadKBps;
        }

        private interface IStatusProjectionFieldSpec
        {
            void CopyConfig(StatusProjectionConfiguration source, StatusProjectionConfiguration destination);
            void CopyRuntime(StatusProjectionSettings source, StatusProjectionConfiguration destination);
            bool ConfigEquivalent(StatusProjectionConfiguration left, StatusProjectionConfiguration right);
            void ApplyConfiguredValue(StatusProjectionConfiguration config, StatusProjectionSettings.Builder builder);
        }

        private sealed class FieldSpec<TConfig, TRuntime>(
            Func<StatusProjectionConfiguration, TConfig> readConfig,
            Action<StatusProjectionConfiguration, TConfig> writeConfig,
            Func<TConfig, TRuntime> resolveRuntime,
            Func<TRuntime, TConfig> serializeConfig,
            Func<StatusProjectionSettings, TRuntime> readRuntime,
            Action<StatusProjectionSettings.Builder, TRuntime> writeRuntime,
            Func<TConfig, TConfig>? cloneConfig = null,
            Func<TConfig, TConfig, bool>? configEquals = null) : IStatusProjectionFieldSpec
        {
            private static readonly Func<TConfig, TConfig> DefaultCloneConfig = static value => value;
            private static readonly Func<TConfig, TConfig, bool> DefaultConfigEquals = EqualityComparer<TConfig>.Default.Equals;
            private readonly Func<TConfig, TConfig> cloneConfig = cloneConfig ?? DefaultCloneConfig;
            private readonly Func<TConfig, TConfig, bool> configEquals = configEquals ?? DefaultConfigEquals;

            public void CopyConfig(StatusProjectionConfiguration source, StatusProjectionConfiguration destination) {
                writeConfig(destination, cloneConfig(readConfig(source)));
            }

            public void CopyRuntime(StatusProjectionSettings source, StatusProjectionConfiguration destination) {
                writeConfig(destination, cloneConfig(serializeConfig(readRuntime(source))));
            }

            public bool ConfigEquivalent(StatusProjectionConfiguration left, StatusProjectionConfiguration right) {
                return configEquals(readConfig(left), readConfig(right));
            }

            public void ApplyConfiguredValue(
                StatusProjectionConfiguration config,
                StatusProjectionSettings.Builder builder) {
                writeRuntime(builder, resolveRuntime(readConfig(config)));
            }
        }
    }
}
