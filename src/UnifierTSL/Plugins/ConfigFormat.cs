namespace UnifierTSL.Plugins
{
    /// <summary>
    /// The built-in config formats that the framework supports by default.
    /// </summary>
    public enum ConfigFormat
    {
        /// <summary>Default JSON format (could map to a default serializer, e.g., System.Text.Json).</summary>
        Json,

        /// <summary>Explicit System.Text.Json serializer.</summary>
        SystemTextJson,

        /// <summary>Explicit Newtonsoft.Json serializer.</summary>
        NewtonsoftJson,

        /// <summary>TOML format.</summary>
        Toml,
    }
}
