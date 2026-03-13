namespace UnifierTSL.Launcher
{
    internal static class ConsoleStatusSetting
    {
        private static readonly IReadOnlyList<IConsoleStatusFieldSpec> fields = [
            new FieldSpec<double, double>(
                readConfig: static config => config.TargetUps,
                writeConfig: static (config, value) => config.TargetUps = value,
                resolveRuntime: static value => value,
                serializeConfig: static value => value,
                readRuntime: static settings => settings.TargetUps,
                writeRuntime: static (settings, value) => settings with { TargetUps = value }),
            new FieldSpec<double, double>(
                readConfig: static config => config.HealthyUpsDeviation,
                writeConfig: static (config, value) => config.HealthyUpsDeviation = value,
                resolveRuntime: static value => value,
                serializeConfig: static value => value,
                readRuntime: static settings => settings.HealthyUpsDeviation,
                writeRuntime: static (settings, value) => settings with { HealthyUpsDeviation = value }),
            new FieldSpec<double, double>(
                readConfig: static config => config.WarningUpsDeviation,
                writeConfig: static (config, value) => config.WarningUpsDeviation = value,
                resolveRuntime: static value => value,
                serializeConfig: static value => value,
                readRuntime: static settings => settings.WarningUpsDeviation,
                writeRuntime: static (settings, value) => settings with { WarningUpsDeviation = value }),
            new FieldSpec<double, double>(
                readConfig: static config => config.UtilHealthyMax,
                writeConfig: static (config, value) => config.UtilHealthyMax = value,
                resolveRuntime: static value => value,
                serializeConfig: static value => value,
                readRuntime: static settings => settings.UtilHealthyMax,
                writeRuntime: static (settings, value) => settings with { UtilHealthyMax = value }),
            new FieldSpec<double, double>(
                readConfig: static config => config.UtilWarningMax,
                writeConfig: static (config, value) => config.UtilWarningMax = value,
                resolveRuntime: static value => value,
                serializeConfig: static value => value,
                readRuntime: static settings => settings.UtilWarningMax,
                writeRuntime: static (settings, value) => settings with { UtilWarningMax = value }),
            new FieldSpec<int, int>(
                readConfig: static config => config.OnlineWarnRemainingSlots,
                writeConfig: static (config, value) => config.OnlineWarnRemainingSlots = value,
                resolveRuntime: static value => value,
                serializeConfig: static value => value,
                readRuntime: static settings => settings.OnlineWarnRemainingSlots,
                writeRuntime: static (settings, value) => settings with { OnlineWarnRemainingSlots = value }),
            new FieldSpec<int, int>(
                readConfig: static config => config.OnlineBadRemainingSlots,
                writeConfig: static (config, value) => config.OnlineBadRemainingSlots = value,
                resolveRuntime: static value => value,
                serializeConfig: static value => value,
                readRuntime: static settings => settings.OnlineBadRemainingSlots,
                writeRuntime: static (settings, value) => settings with { OnlineBadRemainingSlots = value }),
            new FieldSpec<string, ConsoleStatusBandwidthUnit>(
                readConfig: static config => config.BandwidthUnit ?? "bytes",
                writeConfig: static (config, value) => config.BandwidthUnit = value,
                resolveRuntime: LauncherSettingValues.ResolveConfiguredConsoleStatusBandwidthUnit,
                serializeConfig: LauncherSettingValues.DescribeConsoleStatusBandwidthUnit,
                readRuntime: static settings => settings.BandwidthUnit,
                writeRuntime: static (settings, value) => settings with { BandwidthUnit = value },
                configEquals: static (left, right) => LauncherSettingValues.OrdinalIgnoreCaseEquals(left, right)),
            new FieldSpec<double, double>(
                readConfig: static config => config.BandwidthRolloverThreshold,
                writeConfig: static (config, value) => config.BandwidthRolloverThreshold = value,
                resolveRuntime: LauncherSettingValues.ResolveConfiguredConsoleStatusBandwidthRolloverThreshold,
                serializeConfig: static value => value,
                readRuntime: static settings => settings.BandwidthRolloverThreshold,
                writeRuntime: static (settings, value) => settings with { BandwidthRolloverThreshold = value }),
            new FieldSpec<ConsoleStatusBandwidthThresholds, ConsoleStatusBandwidthThresholds>(
                readConfig: ReadServerBandwidth,
                writeConfig: WriteServerBandwidth,
                resolveRuntime: static value => value,
                serializeConfig: static value => value,
                readRuntime: static settings => settings.ServerBandwidth,
                writeRuntime: static (settings, value) => settings with { ServerBandwidth = value }),
            new FieldSpec<ConsoleStatusBandwidthThresholds, ConsoleStatusBandwidthThresholds>(
                readConfig: ReadLauncherBandwidth,
                writeConfig: WriteLauncherBandwidth,
                resolveRuntime: static value => value,
                serializeConfig: static value => value,
                readRuntime: static settings => settings.LauncherBandwidth,
                writeRuntime: static (settings, value) => settings with { LauncherBandwidth = value }),
        ];

        public static ILauncherSettingSpec Spec { get; } = new ScalarSettingSpec<ConsoleStatusConfiguration, ConsoleStatusSettings>(
            cliBindings: [],
            readOverride: static _ => OptionalValue<ConsoleStatusSettings>.None,
            readConfig: static config => config.Launcher?.ConsoleStatus ?? new ConsoleStatusConfiguration(),
            writeConfig: static (config, value) => config.Launcher.ConsoleStatus = CloneConfiguration(value),
            resolveRuntime: Resolve,
            serializeConfig: Serialize,
            readRuntime: static settings => settings.ConsoleStatus,
            writeRuntime: static (settings, value) => settings with { ConsoleStatus = value },
            applyReload: ApplyReload,
            cloneConfig: CloneConfiguration,
            configEquals: Equivalent);

        public static ConsoleStatusConfiguration CloneConfiguration(ConsoleStatusConfiguration? source) {
            ConsoleStatusConfiguration clone = new();
            ConsoleStatusConfiguration value = source ?? new ConsoleStatusConfiguration();
            foreach (IConsoleStatusFieldSpec field in fields) {
                field.CopyConfig(value, clone);
            }

            return clone;
        }

        public static bool Equivalent(
            ConsoleStatusConfiguration? left,
            ConsoleStatusConfiguration? right) {

            ConsoleStatusConfiguration leftValue = left ?? new ConsoleStatusConfiguration();
            ConsoleStatusConfiguration rightValue = right ?? new ConsoleStatusConfiguration();
            foreach (IConsoleStatusFieldSpec field in fields) {
                if (!field.ConfigEquivalent(leftValue, rightValue)) {
                    return false;
                }
            }

            return true;
        }

        public static ConsoleStatusSettings Resolve(ConsoleStatusConfiguration? configured) {
            ConsoleStatusConfiguration source = configured ?? new ConsoleStatusConfiguration();
            ConsoleStatusSettings settings = new();
            foreach (IConsoleStatusFieldSpec field in fields) {
                settings = field.ApplyConfiguredValue(source, settings);
            }

            return settings;
        }

        public static ConsoleStatusConfiguration Serialize(ConsoleStatusSettings source) {
            ConsoleStatusConfiguration config = new();
            foreach (IConsoleStatusFieldSpec field in fields) {
                field.CopyRuntime(source, config);
            }

            return config;
        }

        private static LauncherRuntimeSettings ApplyReload(
            LauncherRuntimeSettings applied,
            ConsoleStatusSettings current,
            ConsoleStatusSettings desired,
            ReloadContext _) {

            if (current != desired) {
                UnifierApi.Logger.Info(
                    GetString("launcher.consoleStatus changed. Command-line status settings are now using the reloaded values."),
                    category: LauncherCategories.Config);
            }

            return applied with { ConsoleStatus = desired };
        }

        private static ConsoleStatusBandwidthThresholds ReadServerBandwidth(ConsoleStatusConfiguration config) {
            return new ConsoleStatusBandwidthThresholds {
                UpWarnKBps = config.ServerUpWarnKBps,
                UpBadKBps = config.ServerUpBadKBps,
                DownWarnKBps = config.ServerDownWarnKBps,
                DownBadKBps = config.ServerDownBadKBps,
            };
        }

        private static void WriteServerBandwidth(
            ConsoleStatusConfiguration config,
            ConsoleStatusBandwidthThresholds value) {

            config.ServerUpWarnKBps = value.UpWarnKBps;
            config.ServerUpBadKBps = value.UpBadKBps;
            config.ServerDownWarnKBps = value.DownWarnKBps;
            config.ServerDownBadKBps = value.DownBadKBps;
        }

        private static ConsoleStatusBandwidthThresholds ReadLauncherBandwidth(ConsoleStatusConfiguration config) {
            return new ConsoleStatusBandwidthThresholds {
                UpWarnKBps = config.LauncherUpWarnKBps,
                UpBadKBps = config.LauncherUpBadKBps,
                DownWarnKBps = config.LauncherDownWarnKBps,
                DownBadKBps = config.LauncherDownBadKBps,
            };
        }

        private static void WriteLauncherBandwidth(
            ConsoleStatusConfiguration config,
            ConsoleStatusBandwidthThresholds value) {

            config.LauncherUpWarnKBps = value.UpWarnKBps;
            config.LauncherUpBadKBps = value.UpBadKBps;
            config.LauncherDownWarnKBps = value.DownWarnKBps;
            config.LauncherDownBadKBps = value.DownBadKBps;
        }

        private interface IConsoleStatusFieldSpec
        {
            void CopyConfig(ConsoleStatusConfiguration source, ConsoleStatusConfiguration destination);
            void CopyRuntime(ConsoleStatusSettings source, ConsoleStatusConfiguration destination);
            bool ConfigEquivalent(ConsoleStatusConfiguration left, ConsoleStatusConfiguration right);
            ConsoleStatusSettings ApplyConfiguredValue(ConsoleStatusConfiguration config, ConsoleStatusSettings settings);
        }

        private sealed class FieldSpec<TConfig, TRuntime>(
            Func<ConsoleStatusConfiguration, TConfig> readConfig,
            Action<ConsoleStatusConfiguration, TConfig> writeConfig,
            Func<TConfig, TRuntime> resolveRuntime,
            Func<TRuntime, TConfig> serializeConfig,
            Func<ConsoleStatusSettings, TRuntime> readRuntime,
            Func<ConsoleStatusSettings, TRuntime, ConsoleStatusSettings> writeRuntime,
            Func<TConfig, TConfig>? cloneConfig = null,
            Func<TConfig, TConfig, bool>? configEquals = null) : IConsoleStatusFieldSpec
        {
            private static readonly Func<TConfig, TConfig> DefaultCloneConfig = static value => value;
            private static readonly Func<TConfig, TConfig, bool> DefaultConfigEquals = EqualityComparer<TConfig>.Default.Equals;
            private readonly Func<TConfig, TConfig> cloneConfig = cloneConfig ?? DefaultCloneConfig;
            private readonly Func<TConfig, TConfig, bool> configEquals = configEquals ?? DefaultConfigEquals;

            public void CopyConfig(ConsoleStatusConfiguration source, ConsoleStatusConfiguration destination) {
                writeConfig(destination, cloneConfig(readConfig(source)));
            }

            public void CopyRuntime(ConsoleStatusSettings source, ConsoleStatusConfiguration destination) {
                writeConfig(destination, cloneConfig(serializeConfig(readRuntime(source))));
            }

            public bool ConfigEquivalent(ConsoleStatusConfiguration left, ConsoleStatusConfiguration right) {
                return configEquals(readConfig(left), readConfig(right));
            }

            public ConsoleStatusSettings ApplyConfiguredValue(
                ConsoleStatusConfiguration config,
                ConsoleStatusSettings settings) {

                return writeRuntime(settings, resolveRuntime(readConfig(config)));
            }
        }
    }
}
