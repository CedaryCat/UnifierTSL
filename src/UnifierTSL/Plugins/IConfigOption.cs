namespace UnifierTSL.Plugins
{
    public interface IConfigOption
    {
        IConfigOption WithFormat(ConfigFormat format);
        IConfigOption WithFormat<TFormatProvider>() where TFormatProvider : IConfigFormatProvider, new();
        /// <summary>
        /// Specifies how serialization failures are handled.
        /// </summary>
        /// <param name="handling">
        /// Strategy to apply when serialization fails. See <see cref="SerializationFailureHandling"/> for options.
        IConfigOption OnSerializationFailure(SerializationFailureHandling handling);

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
        IConfigOption OnDeserializationFailure(DeserializationFailureHandling handling, bool autoPersistFallback = true);

        /// <summary>
        /// Enables or disables automatic reload when the underlying file changes externally.
        /// </summary>
        /// <param name="enabled">True to enable external-change-triggered reloads.</param>
        IConfigOption TriggerReloadOnExternalChange(bool enabled);
    }
}
