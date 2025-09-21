using Newtonsoft.Json.Linq;
using System.Collections.Immutable;
using UnifierTSL.Plugins;

namespace TShockAPI.Configuration
{
    public class ServerConfigFile<TSettings> where TSettings : class, new()
    {
        private TSettings defaultSetting;
        private readonly IPluginConfigHandle<JObject> defaultSettingHandle;
        private readonly IPluginConfigHandle<Dictionary<string, JObject>> serverSpecificSettingHandle;
        private ImmutableDictionary<string, TSettings> cachedServerSettings;

        public TSettings GlobalSettings => defaultSetting;

        public TSettings GetServerSettings(string serverName) { 
            if (cachedServerSettings.TryGetValue(serverName, out TSettings? value)) {
                return value;
            }
            var setting = defaultSettingHandle.Request().ToObject<TSettings>()!;
            ImmutableInterlocked.TryAdd(ref cachedServerSettings, serverName, setting);
            return setting;
        }

        public void SaveToFile() {
            var defObj = new JObject(defaultSetting);
            defaultSettingHandle.Overwrite(defObj);
            serverSpecificSettingHandle.Overwrite(cachedServerSettings.ToDictionary(pair => pair.Key, pair => {
                var obj = new JObject(pair.Value);
                foreach (var prop in ((IDictionary<string, JToken>)obj).ToArray()) {
                    if (!defObj.TryGetValue(prop.Key, out var value)) {
                        continue;
                    }
                    if (prop.Value.ToString() != value.ToString()) {
                        continue;
                    }
                    obj.Remove(prop.Key);
                }
                return obj;
            }));
        }

        public event Action<ServerConfigFile<TSettings>>? OnConfigRead;

        public ServerConfigFile(IPluginConfigRegistrar configRegistrar, string fileNameWithoutExtension) {

            var defaultFile = fileNameWithoutExtension + ".json";
            var serverSpecificFile = fileNameWithoutExtension + ".override.json";

            defaultSettingHandle = configRegistrar
                .CreateConfigRegistration<JObject>(defaultFile)
                .TriggerReloadOnExternalChange(true)
                .Complete();
            defaultSettingHandle.OnChangedAsync += OnDefaultSettingChanged;

            serverSpecificSettingHandle = configRegistrar
                .CreateConfigRegistration<Dictionary<string, JObject>>(serverSpecificFile)
                .TriggerReloadOnExternalChange(true)
                .Complete();
            serverSpecificSettingHandle.OnChangedAsync += OnServerSettingChanged;

            defaultSetting = defaultSettingHandle.Request().ToObject<TSettings>() 
                ?? throw new Exception($"Unable to load default settings for {defaultFile}");

            var builder = ImmutableDictionary.CreateBuilder<string, TSettings>();
            foreach (var overrides in serverSpecificSettingHandle.Request()) {
                var merged = new JObject(defaultSettingHandle.Request());
                merged.Merge(overrides, new JsonMergeSettings {
                    MergeArrayHandling = MergeArrayHandling.Replace,
                    MergeNullValueHandling = MergeNullValueHandling.Ignore
                });
                var overrideData = merged.ToObject<TSettings>();
                if (overrideData is null) {
                    continue;
                }
                builder.Add(overrides.Key, overrideData);
            }
            cachedServerSettings = builder.ToImmutable();
        }

        private ValueTask<bool> OnServerSettingChanged(IPluginConfigHandle<Dictionary<string, JObject>> handle, Dictionary<string, JObject>? serverSettings) {
            if (serverSettings is null) {
                return new ValueTask<bool>(true);
            }
            if (serverSettings.Count == 0) {
                cachedServerSettings = ImmutableDictionary<string, TSettings>.Empty;

                OnConfigRead?.Invoke(this);
                return new ValueTask<bool>(false);
            }
            try {
                var builder = ImmutableDictionary.CreateBuilder<string, TSettings>();
                foreach (var overrides in serverSettings) {
                    var merged = new JObject(defaultSettingHandle.Request());
                    merged.Merge(overrides, new JsonMergeSettings {
                        MergeArrayHandling = MergeArrayHandling.Replace,
                        MergeNullValueHandling = MergeNullValueHandling.Ignore
                    });
                    var overrideData = merged.ToObject<TSettings>();
                    if (overrideData is null) {
                        continue;
                    }
                    builder.Add(overrides.Key, overrideData);
                }
                cachedServerSettings = builder.ToImmutable();
            }
            catch {
                return new ValueTask<bool>(true);
            }

            OnConfigRead?.Invoke(this);
            return new ValueTask<bool>(false);
        }

        private ValueTask<bool> OnDefaultSettingChanged(IPluginConfigHandle<JObject> handle, JObject? config) {
            TSettings? settings = null;
            try {
                settings = config?.ToObject<TSettings>();
            }
            catch { }
            if (settings is null || config is null) {
                return new ValueTask<bool>(true);
            }

            defaultSetting = settings;
            var serverSettings = serverSpecificSettingHandle.Request();
            if (serverSettings.Count == 0) {

                OnConfigRead?.Invoke(this);
                return new ValueTask<bool>(false);
            }
            try {
                var builder = ImmutableDictionary.CreateBuilder<string, TSettings>();
                foreach (var overrides in serverSettings) {
                    var merged = new JObject(config);
                    merged.Merge(overrides, new JsonMergeSettings {
                        MergeArrayHandling = MergeArrayHandling.Replace,
                        MergeNullValueHandling = MergeNullValueHandling.Ignore
                    });
                    var overrideData = merged.ToObject<TSettings>();
                    if (overrideData is null) {
                        continue;
                    }
                    builder.Add(overrides.Key, overrideData);
                }
                cachedServerSettings = builder.ToImmutable();
            }
            catch {
                return new ValueTask<bool>(true);
            }

            OnConfigRead?.Invoke(this);
            return new ValueTask<bool>(false);
        }
    }
}
