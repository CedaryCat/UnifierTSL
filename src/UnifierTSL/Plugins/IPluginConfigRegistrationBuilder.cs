namespace UnifierTSL.Plugins
{
    /// <summary>
    /// Fluent builder used during configuration registration to specify defaults, validation, and reload behavior.
    /// </summary>
    /// <typeparam name="TConfig">The configuration object type.</typeparam>
    public interface IPluginConfigRegistrationBuilder<TConfig> where TConfig : class
    {
        /// <summary>
        /// Specifies a factory to produce the default configuration if none exists or deserialization fails.
        /// </summary>
        /// <param name="factory">Factory function creating a default config instance.</param>
        /// <returns>Builder for chaining.</returns>
        IPluginConfigRegistrationBuilder<TConfig> WithDefault(Func<TConfig> factory);

        /// <summary>
        /// Specifies how serialization failures are handled.
        /// </summary>
        /// <param name="handling">
        /// Strategy to apply when serialization fails. See <see cref="SerializationFailureHandling"/> for options.
        /// <returns>Builder for chaining.</returns>
        IPluginConfigRegistrationBuilder<TConfig> OnSerializationFailure(SerializationFailureHandling handling);

        /// <summary>
        /// Specifies how deserialization failures are handled.
        /// </summary>
        /// <param name="handling">
        /// Strategy to apply when deserialization fails. See <see cref="DeserializationFailureHandling"/> for options.
        /// </param>
        /// <param name="autoPersistFallback">
        /// If true (the default), the fallback object (e.g., null, empty object, new instance) will be written back,
        /// overwriting the original source to keep persisted data consistent. If false, the fallback is used only in memory.
        /// </param>
        /// <returns>Builder for chaining.</returns>
        IPluginConfigRegistrationBuilder<TConfig> OnDeserializationFailure(DeserializationFailureHandling handling, bool autoPersistFallback = true);

        /// <summary>
        /// Enables or disables automatic reload when the underlying file changes externally.
        /// </summary>
        /// <param name="enabled">True to enable external-change-triggered reloads.</param>
        /// <returns>Builder for chaining.</returns>
        IPluginConfigRegistrationBuilder<TConfig> TriggerReloadOnExternalChange(bool enabled);

        /// <summary>
        /// Adds a validation step that runs after loading. If the validator returns false, the config is considered invalid.
        /// </summary>
        /// <param name="validator">Function that validates the config. Returns true if valid.</param>
        /// <returns>Builder for chaining.</returns>
        IPluginConfigRegistrationBuilder<TConfig> WithValidation(Func<TConfig, bool> validator);

        /// <summary>
        /// Finalizes the registration and returns a handle for interacting with the config.
        /// </summary>
        /// <returns>Config handle.</returns>
        IPluginConfigHandle<TConfig> Complete();
    }
}
