using UnifierTSL.Plugins;
using UnifierTSL.PluginService;
namespace UnifierTSL.PluginHost.Configs
{
    internal class ConfigRegistrar(IPluginContainer plugin, string configsPath) : IPluginConfigRegistrar
    {
        private readonly ConfigOption option = new();
        public IConfigOption DefaultOption => option;
        public string Directory => configsPath;

        public IConfigRegistrationBuilder<TConfig> CreateConfigRegistration<TConfig>(string relativePath) where TConfig : class, new() {
            return new ConfigRegistrationBuilder<TConfig>(plugin, configsPath, relativePath, option);
        }
        public IConfigRegistrationBuilder<TConfig> CreateConfigRegistration<TConfig>(string relativePath, ConfigFormat format) where TConfig : class, new() {
            return new ConfigRegistrationBuilder<TConfig>(plugin, configsPath, relativePath, option, ConfigOption.GetFormatter(format));
        }

        public IConfigRegistrationBuilder<TConfig> CreateConfigRegistration<TFormatProvider, TConfig>(string relativePath)
            where TFormatProvider : IConfigFormatProvider, new()
            where TConfig : class, new() {
            return new ConfigRegistrationBuilder<TConfig>(plugin, configsPath, relativePath, option, new TFormatProvider());
        }
    }
}
