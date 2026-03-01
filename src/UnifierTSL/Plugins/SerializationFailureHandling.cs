namespace UnifierTSL.Plugins
{
    /// <summary>
    /// Defines how the system handles a failure during serialization.
    /// </summary>
    public enum SerializationFailureHandling
    {
        /// <summary>
        /// Propagate the error by throwing the exception. No fallback is applied; the failure is surfaced to the caller.
        /// </summary>
        ThrowException,

        /// <summary>
        /// Serialize the value as a JSON null literal (i.e., "null") instead of the intended content.
        /// </summary>
        WriteNull,

        /// <summary>
        /// Serialize the value as an empty object (i.e., "{}") as a fallback.
        /// </summary>
        WriteEmptyInstance,

        /// <summary>
        /// Serialize a newly constructed default instance in place of the problematic object.
        /// </summary>
        WriteNewInstance,
    }
}
