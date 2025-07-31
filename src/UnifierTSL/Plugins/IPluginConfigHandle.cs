namespace UnifierTSL.Plugins
{
    /// <summary>
    /// Handle through which clients can read, write, reload, and observe changes to a plugin configuration.
    /// </summary>
    /// <typeparam name="TConfig">The configuration type.</typeparam>
    public interface IPluginConfigHandle<TConfig> where TConfig : class
    {
        /// <summary>
        /// Asynchronously requests the current configuration.
        /// </summary>
        /// <param name="reloadFromIO">
        /// If true, forces re-reading from the underlying storage even if a cached version exists.
        /// If false, may return a cached copy if available.
        /// </param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The current configuration instance.</returns>
        Task<TConfig?> RequestAsync(bool reloadFromIO = false, CancellationToken cancellationToken = default);

        /// <summary>
        /// Synchronously requests the current configuration.
        /// </summary>
        /// <param name="reloadFromIO">
        /// If true, forces re-reading from the underlying storage even if a cached version exists.
        /// If false, may return a cached copy if available.
        /// </param>
        /// <returns>The current configuration instance.</returns>
        TConfig? Request(bool reloadFromIO = false);

        /// <summary>
        /// Synchronously attempts to get the current configuration if it exists without creating/loading a fresh one.
        /// </summary>
        /// <returns>The current configuration or null if none is loaded.</returns>
        TConfig? TryGetCurrent();

        /// <summary>
        /// Asynchronously overwrites the configuration in storage with a new one.
        /// </summary>
        /// <param name="newConfig">New config to persist.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task OverwriteAsync(TConfig? newConfig, CancellationToken cancellationToken = default);

        /// <summary>
        /// Synchronously overwrites the configuration in storage with a new one.
        /// </summary>
        /// <param name="newConfig">New config to persist.</param>
        void Overwrite(TConfig? newConfig);

        /// <summary>
        /// Asynchronously reloads the configuration from underlying storage, ignoring any cached copy.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task ReloadAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Synchronously reloads the configuration from underlying storage, ignoring any cached copy.
        /// </summary>
        void Reload();

        /// <summary>
        /// Atomically modify the current configuration in memory (without writing to disk).
        /// </summary>
        /// <param name="updater"></param>
        /// <returns></returns>
        TConfig? ModifyInMemory(Func<TConfig?, TConfig?> updater);

        /// <summary>
        /// Change notification. Return true to indicate that the change has been handled and the cache should not be updated again.
        /// </summary>
        event AsyncConfigChangedHandler<TConfig?> OnChangedAsync;

        /// <summary>
        /// Full file path of the underlying configuration file.
        /// </summary>
        string FilePath { get; }
    }
    public delegate ValueTask<bool> AsyncConfigChangedHandler<TConfig>(TConfig config) where TConfig : class?;
}
