namespace UnifierTSL.Launcher
{
    internal static class LauncherSettingRegistry
    {
        private static readonly IReadOnlyList<ILauncherSettingSpec> all = [
            ScalarSettings.LogMode,
            ScalarSettings.ListenPort,
            ScalarSettings.ServerPassword,
            ScalarSettings.JoinServer,
            ScalarSettings.ColorfulConsoleStatus,
            ConsoleStatusSetting.Spec,
            AutoStartServers.Spec,
        ];

        private static readonly Dictionary<string, CliBinding> cliBindings = BuildCliBindings(all);

        public static LauncherCliOverrides ParseOverrides(string[] launcherArgs) {
            LauncherCliOverrides overrides = new();
            Dictionary<string, List<string>> args = Utilities.CLI.ParseArguments(launcherArgs);
            CliParseState state = new();

            foreach (KeyValuePair<string, List<string>> arg in args) {
                if (cliBindings.TryGetValue(arg.Key, out CliBinding? binding)) {
                    binding.Apply(arg.Key, arg.Value, overrides, state);
                }
            }

            return overrides;
        }

        public static RootLauncherConfiguration CloneConfig(RootLauncherConfiguration source) {
            RootLauncherConfiguration clone = new();
            foreach (ILauncherSettingSpec spec in all) {
                spec.CopyConfig(source, clone);
            }

            return clone;
        }

        public static bool ConfigEquivalent(
            RootLauncherConfiguration left,
            RootLauncherConfiguration right) {

            foreach (ILauncherSettingSpec spec in all) {
                if (!spec.ConfigEquivalent(left, right)) {
                    return false;
                }
            }

            return true;
        }

        public static void ApplyOverrides(
            RootLauncherConfiguration config,
            LauncherCliOverrides overrides) {

            foreach (ILauncherSettingSpec spec in all) {
                spec.ApplyOverride(config, overrides);
            }
        }

        public static LauncherRuntimeSettings ResolveRuntimeSettings(RootLauncherConfiguration config) {
            LauncherRuntimeSettings settings = new();
            foreach (ILauncherSettingSpec spec in all) {
                settings = spec.ApplyConfiguredValue(config, settings);
            }

            return settings;
        }

        public static LauncherRuntimeSettings ApplyInteractiveInput(
            LauncherRuntimeSettings current,
            InteractiveInput input) {

            LauncherRuntimeSettings applied = current;
            foreach (ILauncherSettingSpec spec in all) {
                applied = spec.ApplyInteractiveInput(applied, input);
            }

            return applied;
        }

        public static LauncherRuntimeSettings ApplyReload(
            LauncherRuntimeSettings current,
            LauncherRuntimeSettings desired,
            ReloadContext context) {

            LauncherRuntimeSettings applied = current;
            foreach (ILauncherSettingSpec spec in all) {
                applied = spec.ApplyReload(applied, current, desired, context);
            }

            return applied;
        }

        private static Dictionary<string, CliBinding> BuildCliBindings(IEnumerable<ILauncherSettingSpec> specs) {
            Dictionary<string, CliBinding> bindings = new(StringComparer.Ordinal);
            foreach (ILauncherSettingSpec spec in specs) {
                foreach (CliBinding binding in spec.CliBindings) {
                    foreach (string name in binding.Names) {
                        if (!bindings.TryAdd(name, binding)) {
                            throw new InvalidOperationException(
                                GetParticularString("{0} is CLI option name", $"Duplicate launcher CLI binding registered for '{name}'."));
                        }
                    }
                }
            }

            return bindings;
        }
    }
}
