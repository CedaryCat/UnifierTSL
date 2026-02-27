using System.Text.Json;
using System.Text.Json.Serialization;
using UnifierTSL.Plugins;

namespace UnifierTSL.PluginHost.ConfigFormats
{
    public class SystemTextJsonFormatter : IConfigFormatProvider
    {
        public string Name => "SystemTextJson.json";
        public string NullText => "null";
        public string EmptyInstanceText => "{}";

        private readonly JsonSerializerOptions _options;

        public SystemTextJsonFormatter() {
            _options = new JsonSerializerOptions {
                WriteIndented = true,
                PropertyNameCaseInsensitive = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            };
        }

        public string Serialize<TConfig>(TConfig? config) where TConfig : class {
            return JsonSerializer.Serialize(config, _options);
        }

        public TConfig? Deserialize<TConfig>(string content) where TConfig : class, new() {
            if (string.IsNullOrWhiteSpace(content)) return null;
            return JsonSerializer.Deserialize<TConfig>(content, _options);
        }
    }
}
