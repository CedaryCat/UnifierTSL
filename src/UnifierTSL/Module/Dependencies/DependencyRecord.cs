using NuGet.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UnifierTSL.Module.Dependencies
{
    public sealed class DependencyRecord
    {
        public required string Name { get; set; }
        [JsonConverter(typeof(NuGetVersionJsonConverter))]
        public required NuGetVersion Version { get; set; }
        public required List<DependencyItem> Manifests { get; set; }
    }
    public sealed class DependencyItem
    {
        public DependencyItem() {
            FilePath = null!;
            Version = null!;
        }
        public DependencyItem(string filePath, NuGetVersion version) {
            FilePath = filePath;
            Version = version;
        }
        public string FilePath { get; set; }
        [JsonConverter(typeof(NuGetVersionJsonConverter))]
        public NuGetVersion Version { get; set; }
        public bool Obsolete { get; set; }
    }
    public sealed class NuGetVersionJsonConverter : JsonConverter<NuGetVersion>
    {
        public override NuGetVersion? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
            string? versionString = reader.GetString();
            return versionString != null ? NuGetVersion.Parse(versionString) : null;
        }

        public override void Write(Utf8JsonWriter writer, NuGetVersion value, JsonSerializerOptions options) {
            writer.WriteStringValue(value.ToNormalizedString());
        }
    }
}
