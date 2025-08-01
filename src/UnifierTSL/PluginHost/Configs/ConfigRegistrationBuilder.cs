using UnifierTSL.Plugins;
using UnifierTSL.PluginService;
namespace UnifierTSL.PluginHost.Configs
{
    internal class ConfigRegistrationBuilder<TConfig>(
        IPluginContainer plugin, 
        string configsPath, 
        string relativePath, 
        IConfigFormatProvider formatProvider) : IPluginConfigRegistrationBuilder<TConfig> where TConfig : class, new()
    {
        bool AutoReloadOnExternalChange;
        DeserializationFailureHandling DeseriFailureHandling = DeserializationFailureHandling.ThrowException;
        bool DeseriAutoPersistFallback;
        SerializationFailureHandling SeriFailureHandling = SerializationFailureHandling.ThrowException;
        Func<TConfig>? DefaultFactory;
        Func<TConfig, bool>? Validator;

        public IPluginConfigHandle<TConfig> Complete() {
            return new ConfigHandle<TConfig>(
                configsPath,
                plugin,
                formatProvider,
                new(
                    relativePath, 
                    AutoReloadOnExternalChange, 
                    DeseriFailureHandling,
                    DeseriAutoPersistFallback,
                    SeriFailureHandling, 
                    DefaultFactory ?? (static () => new TConfig()),
                    Validator));
        }

        public IPluginConfigRegistrationBuilder<TConfig> TriggerReloadOnExternalChange(bool enabled) {
            AutoReloadOnExternalChange = enabled;
            return this;
        }

        public IPluginConfigRegistrationBuilder<TConfig> OnDeserializationFailure(DeserializationFailureHandling handling, bool autoPersistFallback = true) {
            DeseriFailureHandling = handling;
            DeseriAutoPersistFallback = autoPersistFallback;
            return this;
        }

        public IPluginConfigRegistrationBuilder<TConfig> OnSerializationFailure(SerializationFailureHandling handling) {
            SeriFailureHandling = handling;
            return this;
        }

        public IPluginConfigRegistrationBuilder<TConfig> WithDefault(Func<TConfig> factory) {
            ArgumentNullException.ThrowIfNull(factory);
            DefaultFactory = factory;
            return this;
        }

        public IPluginConfigRegistrationBuilder<TConfig> WithValidation(Func<TConfig, bool> validator) {
            Validator = validator;
            return this;
        }
    }
}
