namespace UnifierTSL.Plugins
{
    /// <summary>
    /// Provides serialization/deserialization logic for a custom config format.
    /// Implementations should be registered/resolved externally (e.g., via DI) rather than using a static singleton pattern.
    /// </summary>
    public interface IConfigFormatProvider
    {
        /// <summary>
        /// Gets the unique name or identifier of this format (e.g., "yaml", "ini").
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Serializes the config object to a string representation suitable for storage, no handling any exception.
        /// </summary>
        /// <typeparam name="TConfig">Type of the config. it can be null</typeparam>
        /// <param name="config">Config instance to serialize.</param>
        /// <returns>Serialized representation.</returns>
        string Serialize<TConfig>(TConfig? config) where TConfig : class;

        /// <summary>
        /// Deserializes the given content string into a config object, no handling any exception.
        /// </summary>
        /// <typeparam name="TConfig">Target config type.</typeparam>
        /// <param name="content">Raw content to deserialize.</param>
        /// <returns>Deserialized object.</returns>
        TConfig? Deserialize<TConfig>(string content) where TConfig : class, new();

        string NullText { get; }
        string EmptyInstanceText { get; }
    }
}
