namespace UnifierTSL.Launcher
{
    internal static class LauncherCategories
    {
        public const string Launcher = "Launcher";
        public const string Config = "Config";
        public const string Logging = "Logging";
    }

    internal interface ILauncherSettingSpec
    {
        IReadOnlyList<CliBinding> CliBindings { get; }
        void CopyConfig(RootLauncherConfiguration source, RootLauncherConfiguration destination);
        bool ConfigEquivalent(RootLauncherConfiguration left, RootLauncherConfiguration right);
        void ApplyOverride(RootLauncherConfiguration config, LauncherCliOverrides overrides);
        void ApplyConfiguredValue(RootLauncherConfiguration config, LauncherRuntimeSettings.Builder builder);
        void ApplyInteractiveInput(LauncherRuntimeSettings.Builder builder, InteractiveInput input);
        void ApplyReload(
            LauncherRuntimeSettings.Builder builder,
            LauncherRuntimeSettings current,
            LauncherRuntimeSettings desired,
            ReloadContext context);
    }

    internal sealed class ScalarSettingSpec<TConfig, TRuntime>(
        IReadOnlyList<CliBinding>? cliBindings,
        Func<LauncherCliOverrides, OptionalValue<TRuntime>> readOverride,
        Func<RootLauncherConfiguration, TConfig> readConfig,
        Action<RootLauncherConfiguration, TConfig> writeConfig,
        Func<TConfig, TRuntime> resolveRuntime,
        Func<TRuntime, TConfig> serializeConfig,
        Func<LauncherRuntimeSettings, TRuntime> readRuntime,
        Action<LauncherRuntimeSettings.Builder, TRuntime> writeRuntime,
        Action<LauncherRuntimeSettings.Builder, TRuntime, TRuntime, ReloadContext> applyReload,
        Func<TConfig, TConfig>? cloneConfig = null,
        Func<TConfig, TConfig, bool>? configEquals = null,
        Func<InteractiveInput, OptionalValue<TRuntime>>? readInteractiveValue = null) : ILauncherSettingSpec
    {
        private static readonly Func<TConfig, TConfig> DefaultCloneConfig = static value => value;
        private static readonly Func<TConfig, TConfig, bool> DefaultConfigEquals = EqualityComparer<TConfig>.Default.Equals;
        private static readonly Func<InteractiveInput, OptionalValue<TRuntime>> DefaultReadInteractiveValue = static _ => OptionalValue<TRuntime>.None;
        private readonly Func<TConfig, TConfig> cloneConfig = cloneConfig ?? DefaultCloneConfig;
        private readonly Func<TConfig, TConfig, bool> configEquals = configEquals ?? DefaultConfigEquals;
        private readonly Func<InteractiveInput, OptionalValue<TRuntime>> readInteractiveValue = readInteractiveValue ?? DefaultReadInteractiveValue;

        public IReadOnlyList<CliBinding> CliBindings { get; } = cliBindings ?? [];

        public void CopyConfig(RootLauncherConfiguration source, RootLauncherConfiguration destination) {
            writeConfig(destination, cloneConfig(readConfig(source)));
        }

        public bool ConfigEquivalent(RootLauncherConfiguration left, RootLauncherConfiguration right) {
            return configEquals(readConfig(left), readConfig(right));
        }

        public void ApplyOverride(RootLauncherConfiguration config, LauncherCliOverrides overrides) {
            OptionalValue<TRuntime> overrideValue = readOverride(overrides);
            if (overrideValue.HasValue) {
                writeConfig(config, serializeConfig(overrideValue.Value));
            }
        }

        public void ApplyConfiguredValue(
            RootLauncherConfiguration config,
            LauncherRuntimeSettings.Builder builder) {
            writeRuntime(builder, resolveRuntime(readConfig(config)));
        }

        public void ApplyInteractiveInput(
            LauncherRuntimeSettings.Builder builder,
            InteractiveInput input) {
            OptionalValue<TRuntime> interactiveValue = readInteractiveValue(input);
            if (!interactiveValue.HasValue) {
                return;
            }

            writeRuntime(builder, interactiveValue.Value);
        }

        public void ApplyReload(
            LauncherRuntimeSettings.Builder builder,
            LauncherRuntimeSettings current,
            LauncherRuntimeSettings desired,
            ReloadContext context) {
            applyReload(
                builder,
                readRuntime(current),
                readRuntime(desired),
                context);
        }
    }

    internal static class LauncherCliBindings
    {
        public static CliBinding CreateSingleValueBinding<T>(
            IReadOnlyList<string> names,
            Func<string, OptionalValue<T>> parseValue,
            Action<LauncherCliOverrides, T> setOverride,
            Func<CliParseState, bool>? shouldApply = null,
            Action<CliParseState>? onApplied = null) {

            return new CliBinding(
                names,
                (_, values, overrides, state) => {
                    if (shouldApply is not null && !shouldApply(state)) {
                        return;
                    }

                    OptionalValue<T> parsed = parseValue(values[0]);
                    if (!parsed.HasValue) {
                        return;
                    }

                    setOverride(overrides, parsed.Value);
                    onApplied?.Invoke(state);
                });
        }

        public static CliBinding CreateFlagBinding<T>(
            IReadOnlyList<string> names,
            T value,
            Action<LauncherCliOverrides, T> setOverride) {

            return new CliBinding(
                names,
                (_, _, overrides, _) => setOverride(overrides, value));
        }
    }

    internal sealed record CliBinding(
        IReadOnlyList<string> Names,
        Action<string, List<string>, LauncherCliOverrides, CliParseState> Apply);

    internal sealed class CliParseState
    {
        public bool JoinServerConfigured { get; set; }
    }

    internal readonly record struct InteractiveInput(int ListenPort, string? ServerPassword);

    internal readonly record struct ReloadContext(
        Action<int> ApplyListenPort,
        Action<string> ApplyServerPassword);

    internal readonly record struct OptionalValue<T>(bool HasValue, T Value)
    {
        public static OptionalValue<T> None => default;

        public static OptionalValue<T> Some(T value) {
            return new OptionalValue<T>(true, value);
        }
    }

    internal readonly record struct AutoStartMergeCandidate(
        AutoStartServerConfiguration Server,
        string Source,
        int Priority,
        int Order);
}
