namespace UnifierTSL.Plugins
{
    /// <summary>
    /// Defines how the system handles a failure during deserialization.
    /// </summary>
    public enum DeserializationFailureHandling
    {
        /// <summary>
        /// Propagate the error by throwing the exception. No fallback is applied; the failure is surfaced to the caller.
        /// </summary>
        ThrowException,

        /// <summary>
        /// Return null as the fallback result.
        /// </summary>
        ReturnNull,

        /// <summary>
        /// Return an empty object (e.g., default-initialized data structure) as the fallback.
        /// </summary>
        ReturnEmptyObject,

        /// <summary>
        /// Return a newly constructed default instance as the fallback.
        /// </summary>
        ReturnNewInstance,
    }
}
