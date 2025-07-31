using Newtonsoft.Json;
using UnifierTSL.Plugins;

namespace UnifierTSL.PluginHost.ConfigFormats
{
    public class NewtonsoftJsonFormater : IConfigFormatProvider
    {
        public string Name => "NewtonsoftJson.json";
        public string NullText => "null";
        public string EmptyInstanceText => "{}";

        private readonly JsonSerializerSettings _settings;

        public NewtonsoftJsonFormater() {
            _settings = new JsonSerializerSettings {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore,
            };
        }

        public string Serialize<TConfig>(TConfig? config) where TConfig : class {
            return JsonConvert.SerializeObject(config, _settings);
        }

        public TConfig? Deserialize<TConfig>(string content) where TConfig : class, new() {
            if (string.IsNullOrWhiteSpace(content)) return null;
            return JsonConvert.DeserializeObject<TConfig>(content, _settings);
        }
    }
}
