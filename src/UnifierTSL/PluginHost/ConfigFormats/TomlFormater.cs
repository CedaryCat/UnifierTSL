using Tomlyn;
using Tomlyn.Syntax;
using UnifierTSL.Plugins;

namespace UnifierTSL.PluginHost.ConfigFormats
{
    public class TomlFormater : IConfigFormatProvider
    {
        public string Name => "toml";
        public string NullText => "";
        public string EmptyInstanceText => "";

        public string Serialize<TConfig>(TConfig? config) where TConfig : class {
            // If given config is null, create an empty object because TOML doesn't allow null
            var toml = Toml.FromModel(config ?? new object());
            return toml.ToString();
        }

        public TConfig? Deserialize<TConfig>(string content) where TConfig : class, new() {
            if (string.IsNullOrWhiteSpace(content)) return null;

            var doc = Toml.Parse(content);
            if (doc.HasErrors) {
                var errors = doc.Diagnostics
                     .Where(d => d.Kind == DiagnosticMessageKind.Error)
                     .Select(d => d.ToString());

                throw new Exception($"TOML parsing errors: \r\n{string.Join("\r\n\r\n", errors)}");
            }

            return doc.ToModel<TConfig>();
        }
    }
}
