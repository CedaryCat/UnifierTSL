using UnifierTSL.FileSystem;
using UnifierTSL.Logging;
using UnifierTSL.Plugins;
using UnifierTSL.PluginService;

namespace UnifierTSL.PluginHost.Configs
{
    internal class ConfigHandle<TConfig> : IPluginConfigHandle<TConfig>
        where TConfig : class, new()
    {
        public record ConfigOption(
            string RelativePath,
            bool AutoReloadOnExternalChange,
            DeserializationFailureHandling DeseriFailureHandling,
            bool DeseriAutoPersistFallback,
            SerializationFailureHandling SeriFailureHandling,
            Func<TConfig> DefaultFactory,
            Func<TConfig, bool>? Validator
        );

        private record LoggerHost(string Name, string? CurrentLogCategory) : ILoggerHost;
        private readonly RoleLogger Logger;
        private readonly IPluginContainer Owner;
        private readonly IConfigFormatProvider FormatProvider;
        private readonly ConfigOption Option;
        private readonly string filePath;

        private readonly IFileMonitorHandle monitor;

        private TConfig? CachedConfig = null;

        public ConfigHandle(
            string configsPath,
            IPluginContainer plugin,
            IConfigFormatProvider formatProvider,
            ConfigOption option) {

            Logger = UnifierApi.CreateLogger(new LoggerHost("PluginConfig", $"P:{plugin.Name}"));
            Owner = plugin;
            FormatProvider = formatProvider;
            Option = option;
            filePath = Path.Combine(configsPath, option.RelativePath);

            monitor = UnifierApi.FileMonitor.Register(filePath, OnFileChanged, OnError);
        }

        private void OnError(object sender, ErrorEventArgs e) {
            Logger.LogHandledExceptionWithMetadata(
                message: GetParticularString("{0} is config file path (relative path)", $"FileWatcher error occurred for config '{Option.RelativePath}'."),
                ex: e.GetException(),
                [new("PluginFile", Owner.Location.FilePath)]);
        }

        private async void OnFileChanged(object sender, FileSystemEventArgs e) {
            string text;
            try {
                text = File.ReadAllText(e.FullPath);
            }
            catch (Exception ex) {
                Logger.LogHandledExceptionWithMetadata(
                    message: GetParticularString("{0} is config file path (relative path)", $"Failed to read modified config '{Option.RelativePath}', skipping reload."),
                    ex: ex,
                    [new("PluginFile", Owner.Location.FilePath)]);
                return;
            }
            TConfig? newConfig = ReturnDeserializedOrCache(text, null);

            if (OnChangedAsync is not null) {
                foreach (AsyncConfigChangedHandler<TConfig> executor in OnChangedAsync.GetInvocationList().Cast<AsyncConfigChangedHandler<TConfig>>()) {
                    if (await executor.Invoke(this, newConfig)) {
                        return;
                    }
                }
            }

            if (Option.AutoReloadOnExternalChange) {
                CachedConfig = newConfig;
            }
        }

        /// <summary>
        /// Never call in user code
        /// </summary>
        public void Dispose() {
            monitor.Dispose();
        }

        #region Helper Methods
        private void EnsureDirectoryExists() {
            string dir = Path.GetDirectoryName(filePath)!;
            if (!Directory.Exists(dir)) {
                Directory.CreateDirectory(dir);
            }
        }
        private string SerializeToText(TConfig? newConfig) {
            string result;
            try {
                result = FormatProvider.Serialize(newConfig);
            }
            catch (Exception ex) {
                switch (Option.SeriFailureHandling) {
                    default:
                    case SerializationFailureHandling.ThrowException:
                        throw;
                    case SerializationFailureHandling.WriteNull:
                        result = FormatProvider.NullText;
                        break;
                    case SerializationFailureHandling.WriteEmptyInstance:
                        result = FormatProvider.EmptyInstanceText;
                        break;
                    case SerializationFailureHandling.WriteNewInstance:
                        result = FormatProvider.Serialize(Option.DefaultFactory());
                        break;
                }

                CachedConfig = FormatProvider.Deserialize<TConfig>(result);

                Logger.LogHandledExceptionWithMetadata(
                    message: $"Failed to serialize config: '{Option.RelativePath}'.",
                    ex: ex,
                    metadata: [new("PluginFile", Owner.Location.FilePath)]);
            }

            return result;
        }
        private TConfig? ReturnDeserializedOrCache(string text, Exception? readException) {
            TConfig? result;

            if (readException is not null) {
                switch (Option.DeseriFailureHandling) {
                    default:
                    case DeserializationFailureHandling.ThrowException:
                        throw readException;
                    case DeserializationFailureHandling.ReturnNull:
                        CachedConfig = null;
                        text = null!;
                        break;
                    case DeserializationFailureHandling.ReturnEmptyObject:
                        text = FormatProvider.EmptyInstanceText;
                        break;
                    case DeserializationFailureHandling.ReturnNewInstance:
                        text = FormatProvider.Serialize(Option.DefaultFactory());
                        break;
                }
                Logger.LogHandledExceptionWithMetadata(
                    message: $"Failed to read config: '{Option.RelativePath}' when requesting, return by mode: {Option.DeseriFailureHandling}.",
                    ex: readException,
                    metadata: [new("PluginFile", Owner.Location.FilePath)]);

                if (text is null) {
                    return CachedConfig;
                }
            }

            try {
                result = FormatProvider.Deserialize<TConfig>(text);
            }
            catch (Exception ex) {
                // If readException is not null, the text must be given by FormatProvider and occurred a unexpected exception. so throw.
                if (readException is not null) {
                    throw;
                }
                switch (Option.DeseriFailureHandling) {
                    default:
                    case DeserializationFailureHandling.ThrowException:
                        throw;
                    case DeserializationFailureHandling.ReturnNull:
                        CachedConfig = null;
                        break;
                    case DeserializationFailureHandling.ReturnEmptyObject:
                        CachedConfig = FormatProvider.Deserialize<TConfig>(FormatProvider.EmptyInstanceText);
                        break;
                    case DeserializationFailureHandling.ReturnNewInstance:
                        CachedConfig = Option.DefaultFactory();
                        break;
                }
                Logger.LogHandledExceptionWithMetadata(
                    message: $"Failed to deserialize config: '{Option.RelativePath}' when reloading, skipping reload.",
                    ex: ex,
                    metadata: [new("PluginFile", Owner.Location.FilePath)]);
                return CachedConfig;
            }

            return result;
        }
        #endregion

        #region Implementation
        public string FilePath => filePath;
        public event AsyncConfigChangedHandler<TConfig>? OnChangedAsync;
        public TConfig? ModifyInMemory(Func<TConfig?, TConfig?> updater) => CachedConfig = updater(CachedConfig);
        public void Overwrite(TConfig? newConfig) {
            CachedConfig = newConfig;
            string text = SerializeToText(newConfig);

            EnsureDirectoryExists();

            using (FileLockManager.Enter(filePath)) {
                try {
                    monitor.InternalModify(() => File.WriteAllText(filePath, text));
                }
                catch (Exception ex) {
                    if (Option.SeriFailureHandling is SerializationFailureHandling.ThrowException) {
                        throw;
                    }
                    Logger.LogHandledExceptionWithMetadata(
                        message: $"Failed to write config: '{Option.RelativePath}'.",
                        ex: ex,
                        metadata: [new("PluginFile", Owner.Location.FilePath)]);
                }
            }
        }

        public async Task OverwriteAsync(TConfig? newConfig, CancellationToken cancellationToken = default) {
            CachedConfig = newConfig;
            string text = SerializeToText(newConfig);

            EnsureDirectoryExists();

            using (FileLockManager.Enter(filePath)) {
                try {
                    await monitor.InternalModifyAsync(
                        async () => await File.WriteAllTextAsync(filePath, text, cancellationToken)
                    );
                }
                catch (Exception ex) {
                    if (Option.SeriFailureHandling is SerializationFailureHandling.ThrowException) {
                        throw;
                    }
                    Logger.LogHandledExceptionWithMetadata(
                        message: $"Failed to write config: '{Option.RelativePath}'.",
                        ex: ex,
                        metadata: [new("PluginFile", Owner.Location.FilePath)]);
                }
            }
        }

        public void Reload() {
            EnsureDirectoryExists();

            if (!File.Exists(filePath)) {
                Overwrite(Option.DefaultFactory());
                return;
            }

            string text;
            using (FileLockManager.Enter(filePath)) {
                try {
                    text = File.ReadAllText(filePath);
                }
                catch (Exception ex) {
                    if (Option.SeriFailureHandling is SerializationFailureHandling.ThrowException) {
                        throw;
                    }
                    Logger.LogHandledExceptionWithMetadata(
                        message: $"Failed to read config: '{Option.RelativePath}' when reloading, skipping reload.",
                        ex: ex,
                        metadata: [new("PluginFile", Owner.Location.FilePath)]);
                    return;
                }
            }

            try {
                CachedConfig = FormatProvider.Deserialize<TConfig>(text);
            }
            catch (Exception ex) {
                if (Option.DeseriFailureHandling is DeserializationFailureHandling.ThrowException) {
                    throw;
                }
                Logger.LogHandledExceptionWithMetadata(
                    message: $"Failed to deserialize config: '{Option.RelativePath}' when reloading, skipping reload.",
                    ex: ex,
                    metadata: [new("PluginFile", Owner.Location.FilePath)]);
            }
        }

        public async Task ReloadAsync(CancellationToken cancellationToken = default) {
            EnsureDirectoryExists();

            if (!File.Exists(filePath)) {
                await OverwriteAsync(Option.DefaultFactory(), cancellationToken);
            }

            string text;
            using (FileLockManager.Enter(filePath)) {
                try {
                    text = File.ReadAllText(filePath);
                }
                catch (Exception ex) {
                    if (Option.SeriFailureHandling is SerializationFailureHandling.ThrowException) {
                        throw;
                    }
                    Logger.LogHandledExceptionWithMetadata(
                        message: $"Failed to read config: '{Option.RelativePath}' when reloading, skipping reload.",
                        ex: ex,
                        metadata: [new("PluginFile", Owner.Location.FilePath)]);
                    return;
                }
            }

            try {
                CachedConfig = FormatProvider.Deserialize<TConfig>(text);
            }
            catch (Exception ex) {
                if (Option.DeseriFailureHandling is DeserializationFailureHandling.ThrowException) {
                    throw;
                }
                Logger.LogHandledExceptionWithMetadata(
                    message: $"Failed to deserialize config: '{Option.RelativePath}' when reloading, skipping reload.",
                    ex: ex,
                    metadata: [new("PluginFile", Owner.Location.FilePath)]);
            }
        }

        public TConfig Request(bool reloadFromIO = false) {
            if (CachedConfig is not null && !reloadFromIO) {
                return CachedConfig;
            }

            EnsureDirectoryExists();

            if (!File.Exists(filePath)) {
                Overwrite(Option.DefaultFactory());
            }

            string text;
            Exception? readException = null;
            using (FileLockManager.Enter(filePath)) {
                try {
                    text = File.ReadAllText(filePath);
                }
                catch (Exception ex) {
                    readException = ex;
                    text = null!;
                }
            }

            CachedConfig = ReturnDeserializedOrCache(text, readException);
            if (readException is not null && Option.DeseriAutoPersistFallback) {
                Overwrite(CachedConfig);
            }

            return CachedConfig!;
        }

        public async Task<TConfig> RequestAsync(bool reloadFromIO = false, CancellationToken cancellationToken = default) {
            if (CachedConfig is not null && !reloadFromIO) {
                return CachedConfig;
            }

            EnsureDirectoryExists();

            if (!File.Exists(filePath)) {
                await OverwriteAsync(Option.DefaultFactory(), cancellationToken);
            }

            string text;
            Exception? readException = null;
            using (FileLockManager.Enter(filePath)) {
                try {
                    text = await File.ReadAllTextAsync(filePath, cancellationToken);
                }
                catch (Exception ex) {
                    readException = ex;
                    text = null!;
                }
            }

            CachedConfig = ReturnDeserializedOrCache(text, readException);
            if (readException is not null && Option.DeseriAutoPersistFallback) {
                await OverwriteAsync(CachedConfig, cancellationToken);
            }

            return CachedConfig!;
        }

        public TConfig? TryGetCurrent() => CachedConfig;

        #endregion
    }
}
