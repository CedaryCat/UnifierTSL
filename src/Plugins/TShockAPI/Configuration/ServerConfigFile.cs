using Newtonsoft.Json.Linq;
using System.Collections.Immutable;
using UnifierTSL.Plugins;

namespace TShockAPI.Configuration
{
    public class ServerConfigFile<TSettings> where TSettings : class, new()
    {
        private static readonly JsonMergeSettings PatchMergeSettings = new() {
            MergeArrayHandling = MergeArrayHandling.Replace,
            MergeNullValueHandling = MergeNullValueHandling.Ignore
        };

        private TSettings defaultSetting;
        private readonly IPluginConfigHandle<TSettings> defaultSettingHandle;
        private readonly IPluginConfigHandle<Dictionary<string, JObject>> serverSpecificSettingHandle;
        private ImmutableDictionary<string, TSettings> cachedServerSettings;

        public TSettings GlobalSettings => defaultSetting;

        public TSettings GetServerSettings(string serverName) {
            if (cachedServerSettings.TryGetValue(serverName, out TSettings? value)) {
                return value;
            }
            var setting = CloneSettings(defaultSetting);
            ImmutableInterlocked.TryAdd(ref cachedServerSettings, serverName, setting);
            return setting;
        }

        public void SaveToFile() {
            var globalSnapshot = CloneSettings(defaultSetting);
            var globalObject = ToJObject(globalSnapshot);

            defaultSettingHandle.Overwrite(globalSnapshot);
            serverSpecificSettingHandle.Overwrite(cachedServerSettings.ToDictionary(
                pair => pair.Key,
                pair => CreatePatch(pair.Value, globalObject)));
        }

        public event Action<ServerConfigFile<TSettings>>? OnConfigRead;

        public ServerConfigFile(IPluginConfigRegistrar configRegistrar, string fileNameWithoutExtension) {

            var defaultFile = fileNameWithoutExtension + ".json";
            var serverSpecificFile = fileNameWithoutExtension + ".override.json";

            defaultSettingHandle = configRegistrar
                .CreateConfigRegistration<TSettings>(defaultFile, ConfigFormat.NewtonsoftJson)
                .WithDefault(static () => new TSettings())
                .TriggerReloadOnExternalChange(true)
                .Complete();
            defaultSettingHandle.OnChangedAsync += OnDefaultSettingChanged;

            serverSpecificSettingHandle = configRegistrar
                .CreateConfigRegistration<Dictionary<string, JObject>>(serverSpecificFile, ConfigFormat.NewtonsoftJson)
                .WithDefault(static () => new Dictionary<string, JObject>())
                .TriggerReloadOnExternalChange(true)
                .Complete();
            serverSpecificSettingHandle.OnChangedAsync += OnServerSettingChanged;

            defaultSetting = defaultSettingHandle.Request()
                ?? throw new Exception($"Unable to load default settings for {defaultFile}");
            cachedServerSettings = BuildServerSettings(serverSpecificSettingHandle.Request(), defaultSetting);
        }

        private ValueTask<bool> OnServerSettingChanged(IPluginConfigHandle<Dictionary<string, JObject>> handle, Dictionary<string, JObject>? serverSettings) {
            if (serverSettings is null) {
                return new ValueTask<bool>(true);
            }
            try {
                cachedServerSettings = BuildServerSettings(serverSettings, defaultSetting);
            }
            catch {
                return new ValueTask<bool>(true);
            }

            OnConfigRead?.Invoke(this);
            return new ValueTask<bool>(false);
        }

        private ValueTask<bool> OnDefaultSettingChanged(IPluginConfigHandle<TSettings> handle, TSettings? config) {
            if (config is null) {
                return new ValueTask<bool>(true);
            }

            defaultSetting = config;
            try {
                cachedServerSettings = BuildServerSettings(serverSpecificSettingHandle.Request(), defaultSetting);
            }
            catch {
                return new ValueTask<bool>(true);
            }

            OnConfigRead?.Invoke(this);
            return new ValueTask<bool>(false);
        }

        private static ImmutableDictionary<string, TSettings> BuildServerSettings(
            IReadOnlyDictionary<string, JObject> serverSettings,
            TSettings globalSettings) {

            if (serverSettings.Count == 0) {
                return ImmutableDictionary<string, TSettings>.Empty;
            }

            var builder = ImmutableDictionary.CreateBuilder<string, TSettings>();
            foreach (var overrides in serverSettings) {
                builder.Add(overrides.Key, ApplyPatch(globalSettings, overrides.Value));
            }
            return builder.ToImmutable();
        }

        private static TSettings CloneSettings(TSettings settings) {
            return ToJObject(settings).ToObject<TSettings>()
                ?? throw new InvalidOperationException($"Unable to clone settings for {typeof(TSettings).Name}.");
        }

        private static JObject ToJObject(TSettings settings) {
            return JObject.FromObject(settings);
        }

        private static TSettings ApplyPatch(TSettings globalSettings, JObject patch) {
            var merged = ToJObject(globalSettings);
            merged.Merge(new JObject(patch), PatchMergeSettings);
            return merged.ToObject<TSettings>()
                ?? throw new InvalidOperationException($"Unable to apply server patch for {typeof(TSettings).Name}.");
        }

        private static JObject CreatePatch(TSettings serverSettings, JObject globalSettings) {
            var patch = ToJObject(serverSettings);
            foreach (var property in patch.Properties().ToArray()) {
                if (!globalSettings.TryGetValue(property.Name, out var globalValue)) {
                    continue;
                }

                if (JToken.DeepEquals(property.Value, globalValue)) {
                    property.Remove();
                }
            }
            return patch;
        }
    }
}
