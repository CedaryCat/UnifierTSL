using UnifierTSL.Reflection.Metadata;

namespace UnifierTSL.Plugins
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class PluginMetadataAttribute(string name, string version = "1.0.0.0", string author = "Unknown", string description = "") : Attribute
    {
        public string Name { get; } = name;
        public Version Version { get; } = new Version(version);
        public string Author { get; } = author;
        public string Description { get; } = description;

        public static PluginMetadata FromAttributeMetadata(ParsedCustomAttribute metadataAttr) {
            return new PluginMetadata(
                (string?)metadataAttr.ConstructorArguments[0] ?? throw new ArgumentNullException(nameof(name)),
                new Version((string?)metadataAttr.ConstructorArguments[1] ?? throw new ArgumentNullException(nameof(version))),
                (string?)metadataAttr.ConstructorArguments[2] ?? throw new ArgumentNullException(nameof(author)),
                (string?)metadataAttr.ConstructorArguments[3] ?? throw new ArgumentNullException(nameof(description)));
        }

        public PluginMetadata ToPluginMetadata() {
            return new PluginMetadata(Name, Version, Author, Description);
        }
    }
}