using UnifierTSL.PluginHost.ConfigFormats;
using UnifierTSL.Plugins;
using UnifierTSL.PluginService;
namespace UnifierTSL.PluginHost.Configs
{
    internal class PluginConfigRegistrar(IPluginContainer plugin, string configsPath) : IPluginConfigRegistrar
    {
        public IPluginConfigRegistrationBuilder<TConfig> CreateConfigRegistration<TConfig>(string relativePath, ConfigFormat format) where TConfig : class, new() {
            return format switch {
                ConfigFormat.NewtonsoftJson => CreateConfigRegistration<NewtonsoftJsonFormater, TConfig>(relativePath),

                ConfigFormat.Toml => CreateConfigRegistration<TomlFormater, TConfig>(relativePath),

                ConfigFormat.Json or
                ConfigFormat.SystemTextJson or 
                _ => CreateConfigRegistration<SystemTextJsonFormater, TConfig>(relativePath),
                // _ => throw new NotSupportedException($"Unsupported config format: {format}"),
            };
        }

        public IPluginConfigRegistrationBuilder<TConfig> CreateConfigRegistration<TFormatProvider, TConfig>(string relativePath)
            where TFormatProvider : IConfigFormatProvider, new()
            where TConfig : class, new() {
            return new PluginConfigRegistrationBuilder<TConfig>(plugin, configsPath, relativePath, new TFormatProvider());
        }
    }
}
