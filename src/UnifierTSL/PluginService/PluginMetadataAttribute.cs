using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnifierTSL.PluginService.Dependencies;

namespace UnifierTSL.PluginService.Metadata
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class PluginMetadataAttribute(string name, string version = "1.0.0.0", string author = "Unknown", string description = "") : Attribute
    {
        public string Name { get; } = name;
        public Version Version { get; } = new Version(version);
        public string Author { get; } = author;
        public string Description { get; } = description;
        public PluginMetadata ToPluginMetadata(ModuleDependenciesAttribute? dependenciesMetadata = null) {
            return new PluginMetadata(Name, Version, Author, Description, dependenciesMetadata?.DependenciesProvider);
        }
    }
    public record PluginMetadata(string Name, Version Version, string Author, string Description, IDependencyProvider? DependenciesProvider);
}