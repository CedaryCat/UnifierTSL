using UnifierTSL.Plugins;

namespace TShockAPI.Configuration
{
    public class ConfigFile<TSettings> where TSettings : class, new() {
        private readonly IPluginConfigHandle<TSettings> settingsHandle;
        private TSettings settings;
        public TSettings Settings => settings;

        public event Action<ConfigFile<TSettings>>? OnConfigRead;
        public ConfigFile(IPluginConfigRegistrar configRegistrar, string fileNameWithoutExtension, Func<TSettings> defaultSettingFactory) {
            settingsHandle = configRegistrar
                .CreateConfigRegistration<TSettings>(fileNameWithoutExtension + ".json")
                .WithDefault(defaultSettingFactory)
                .TriggerReloadOnExternalChange(true)
                .Complete();
            settingsHandle.OnChangedAsync += OnSettingsChanged;
            settings = settingsHandle.Request();
        }

        private ValueTask<bool> OnSettingsChanged(IPluginConfigHandle<TSettings> handle, TSettings? config) {
            if (config is null) {
                return new ValueTask<bool>(true);
            }
            settings = config;
            OnConfigRead?.Invoke(this);
            return new ValueTask<bool>(false);
        }
    }
}
