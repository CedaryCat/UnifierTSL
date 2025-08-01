using UnifierTSL.Plugins;
using UnifierTSL.PluginService;
namespace UnifierTSL.PluginHost.Configs
{
    internal class ConfigRegistrationBuilder<TConfig> : IConfigRegistrationBuilder<TConfig> where TConfig : class, new()
    {
        bool AutoReloadOnExternalChange;
        DeserializationFailureHandling DeseriFailureHandling = DeserializationFailureHandling.ThrowException;
        bool DeseriAutoPersistFallback;
        SerializationFailureHandling SeriFailureHandling = SerializationFailureHandling.ThrowException;
        Func<TConfig>? DefaultFactory;
        Func<TConfig, bool>? Validator;
        private IConfigFormatProvider formatProvider;
        private readonly IPluginContainer plugin;
        private readonly string configsPath;
        private readonly string relativePath;

        public ConfigRegistrationBuilder(
            IPluginContainer plugin,
            string configsPath,
            string relativePath,
            ConfigOption option,
            IConfigFormatProvider formatProvider) {

            this.plugin = plugin;
            this.configsPath = configsPath;
            this.relativePath = relativePath;
            this.formatProvider = formatProvider;

            AutoReloadOnExternalChange = option.AutoReloadOnExternalChange;
            DeseriFailureHandling = option.DeseriFailureHandling;
            DeseriAutoPersistFallback = option.DeseriAutoPersistFallback;
            SeriFailureHandling = option.SeriFailureHandling;
        }

        public ConfigRegistrationBuilder(
            IPluginContainer plugin,
            string configsPath,
            string relativePath,
            ConfigOption option) {

            this.plugin = plugin;
            this.configsPath = configsPath;
            this.relativePath = relativePath;

            formatProvider = option.FormatProvider;
            AutoReloadOnExternalChange = option.AutoReloadOnExternalChange;
            DeseriFailureHandling = option.DeseriFailureHandling;
            DeseriAutoPersistFallback = option.DeseriAutoPersistFallback;
            SeriFailureHandling = option.SeriFailureHandling;
        }

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

        IConfigRegistrationBuilder<TConfig> IConfigRegistrationBuilder<TConfig>.TriggerReloadOnExternalChange(bool enabled) {
            AutoReloadOnExternalChange = enabled;
            return this;
        }

        IConfigRegistrationBuilder<TConfig> IConfigRegistrationBuilder<TConfig>.OnDeserializationFailure(DeserializationFailureHandling handling, bool autoPersistFallback) {
            DeseriFailureHandling = handling;
            DeseriAutoPersistFallback = autoPersistFallback;
            return this;
        }

        IConfigRegistrationBuilder<TConfig> IConfigRegistrationBuilder<TConfig>.OnSerializationFailure(SerializationFailureHandling handling) {
            SeriFailureHandling = handling;
            return this;
        }

        IConfigRegistrationBuilder<TConfig> IConfigRegistrationBuilder<TConfig>.WithDefault(Func<TConfig> factory) {
            ArgumentNullException.ThrowIfNull(factory);
            DefaultFactory = factory;
            return this;
        }

        IConfigRegistrationBuilder<TConfig> IConfigRegistrationBuilder<TConfig>.WithValidation(Func<TConfig, bool> validator) {
            Validator = validator;
            return this;
        }
    }
}
