using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnifierTSL.Module;

namespace UnifierTSL.Plugins
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class PluginMetadataAttribute(string name, string version = "1.0.0.0", string author = "Unknown", string description = "") : Attribute
    {
        public string Name { get; } = name;
        public Version Version { get; } = new Version(version);
        public string Author { get; } = author;
        public string Description { get; } = description;
        public PluginMetadata ToPluginMetadata() {
            return new PluginMetadata(Name, Version, Author, Description);
        }
    }
}