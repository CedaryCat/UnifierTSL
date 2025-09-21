using UnifierTSL.PluginHost.ConfigFormats;
using UnifierTSL.Plugins;

namespace UnifierTSL.PluginHost.Configs
{
    internal class ConfigOption : IConfigOption
    {
        public bool AutoReloadOnExternalChange;
        public DeserializationFailureHandling DeseriFailureHandling = DeserializationFailureHandling.ThrowException;
        public bool DeseriAutoPersistFallback;
        public SerializationFailureHandling SeriFailureHandling = SerializationFailureHandling.ThrowException;
        public IConfigFormatProvider FormatProvider = new SystemTextJsonFormater();
        public IConfigOption OnDeserializationFailure(DeserializationFailureHandling handling, bool autoPersistFallback = true) {
            AutoReloadOnExternalChange = autoPersistFallback;
            DeseriFailureHandling = handling;
            return this;
        }

        public IConfigOption OnSerializationFailure(SerializationFailureHandling handling) {
            SeriFailureHandling = handling;
            return this;
        }

        public IConfigOption TriggerReloadOnExternalChange(bool enabled) {
            AutoReloadOnExternalChange = enabled;
            return this;
        }

        public IConfigOption WithFormat(ConfigFormat format) {
            FormatProvider = GetFormater(format);
            return this;
        }

        public IConfigOption WithFormat<TFormatProvider>() where TFormatProvider : IConfigFormatProvider, new() {
            FormatProvider = new TFormatProvider();
            return this;
        }

        public static IConfigFormatProvider GetFormater(ConfigFormat format) {
            return format switch {
                ConfigFormat.NewtonsoftJson => new NewtonsoftJsonFormater(),

                ConfigFormat.Toml => new TomlFormater(),

                ConfigFormat.Json or
                ConfigFormat.SystemTextJson or
                _ => new SystemTextJsonFormater(),
                // _ => throw new NotSupportedException($"Unsupported config format: {format}"),
            };
        }
    }
}
