using UnifierTSL.Launcher;

namespace UnifierTSL
{
    internal sealed class LauncherRuntimeOps
    {
        public LauncherCliOverrides ParseLauncherOverrides(string[] launcherArgs) {
            return LauncherSettingRegistry.ParseOverrides(launcherArgs);
        }

        public RootLauncherConfiguration BuildEffectiveStartupConfiguration(
            RootLauncherConfiguration config,
            LauncherCliOverrides overrides,
            out bool configChanged) {

            ArgumentNullException.ThrowIfNull(config);
            ArgumentNullException.ThrowIfNull(overrides);

            RootLauncherConfiguration effective = LauncherSettingRegistry.CloneConfig(config);
            LauncherSettingRegistry.ApplyOverrides(effective, overrides);
            configChanged = !LauncherSettingRegistry.ConfigEquivalent(config, effective);
            return effective;
        }

        public LauncherRuntimeSettings ResolveRuntimeSettingsFromConfig(RootLauncherConfiguration config) {
            return LauncherSettingRegistry.ResolveRuntimeSettings(config);
        }

        public LauncherRuntimeSettings ApplyReloadedRuntimeSettings(
            LauncherRuntimeSettings current,
            LauncherRuntimeSettings desired,
            Action<int> applyListenPort,
            Action<string> applyServerPassword) {

            ArgumentNullException.ThrowIfNull(applyListenPort);
            ArgumentNullException.ThrowIfNull(applyServerPassword);

            return LauncherSettingRegistry.ApplyReload(
                current,
                desired,
                new ReloadContext(applyListenPort, applyServerPassword));
        }

        public void ApplyAutoStartServers(IEnumerable<AutoStartServerConfiguration> servers) {
            AutoStartServers.ApplyAll(servers);
        }

        public LauncherRuntimeSettings SyncRuntimeSettingsFromInteractiveInput(
            LauncherRuntimeSettings current,
            int listenPort,
            string? serverPassword) {

            return LauncherSettingRegistry.ApplyInteractiveInput(
                current,
                new InteractiveInput(listenPort, serverPassword));
        }
    }
}
