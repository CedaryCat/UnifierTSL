using System.Threading;
using UnifierTSL.Logging;
using UnifierTSL.Plugins;
using UnifierTSL.PluginService;
namespace UnifierTSL.PluginHost.Configs
{
    internal class ConfigHandle<TConfig>(
        string configsPath,
        IPluginContainer plugin,
        IConfigFormatProvider formatProvider, 
        ConfigHandle<TConfig>.ConfigOption option) : IPluginConfigHandle<TConfig>

        where TConfig : class, new() {

        public record ConfigOption(
            string RelativePath,
            bool AutoReloadOnExternalChange,
            DeserializationFailureHandling DeseriFailureHandling,
            bool DeseriAutoPersistFallback,
            SerializationFailureHandling SeriFailureHandling,
            Func<TConfig> DefaultFactory,
            Func<TConfig, bool>? Validator
        );

        record LoggerHost(string Name, string? CurrentLogCategory) : ILoggerHost;
        readonly RoleLogger Logger = UnifierApi.CreateLogger(new LoggerHost("PluginConfig", $"P:{plugin.Name}"));
        readonly IPluginContainer Owner = plugin;
        readonly IConfigFormatProvider FormatProvider = formatProvider;
        readonly ConfigOption Option = option;
        readonly string filePath = Path.Combine(configsPath, option.RelativePath);

        TConfig? CachedConfig = null;

        #region Helper Methods
        void EnsureDirectoryExists() {
            var dir = Path.GetDirectoryName(filePath)!;
            if (!Directory.Exists(dir)) {
                Directory.CreateDirectory(dir);
            }
        }
        string SerializeToText(TConfig? newConfig) {
            string result;
            try {
                CachedConfig = newConfig;
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
                    message: $"Failed to serialize config: {Option.RelativePath}",
                    ex: ex,
                    [new("PluginFile", Owner.Location.FilePath)]);
            }

            return result;
        }
        TConfig? DeserializeToCache(string text, Exception? readException) {
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
                    message: $"Failed to read config: {Option.RelativePath} when requesting, return by mode: {Option.DeseriFailureHandling}",
                    ex: readException,
                    [new("PluginFile", Owner.Location.FilePath)]);

                if (text is null) {
                    return CachedConfig;
                }
            }

            try {
                CachedConfig = FormatProvider.Deserialize<TConfig>(text);
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
                    message: $"Failed to deserialize config: {Option.RelativePath} when reloading, skipping reload.",
                    ex: ex,
                    [new("PluginFile", Owner.Location.FilePath)]);
                return CachedConfig;
            }

            return CachedConfig;
        }
        #endregion

        #region Implementation
        public string FilePath => filePath;

#warning TODO: Implement a file watcher to monitor changes in the config file and trigger reloads if AutoReloadOnExternalChange is true.
        public event AsyncConfigChangedHandler<TConfig?>? OnChangedAsync;
        public TConfig? ModifyInMemory(Func<TConfig?, TConfig?> updater) => CachedConfig = updater(CachedConfig);
        public void Overwrite(TConfig? newConfig) {
            string text = SerializeToText(newConfig);

            EnsureDirectoryExists();

            using (FileLockManager.Enter(filePath)) {
                try {
                    File.WriteAllText(filePath, text);
                }
                catch (Exception ex) {
                    if (Option.SeriFailureHandling is SerializationFailureHandling.ThrowException) {
                        throw;
                    }
                    Logger.LogHandledExceptionWithMetadata(
                        message: $"Failed to write config: {Option.RelativePath}",
                        ex: ex,
                        [new("PluginFile", Owner.Location.FilePath)]);
                }
            }
        }

        public async Task OverwriteAsync(TConfig? newConfig, CancellationToken cancellationToken = default) {
            string text = SerializeToText(newConfig);

            EnsureDirectoryExists();

            using (FileLockManager.Enter(filePath)) {
                try {
                    await File.WriteAllTextAsync(filePath, text, cancellationToken);
                }
                catch (Exception ex) {
                    if (Option.SeriFailureHandling is SerializationFailureHandling.ThrowException) {
                        throw;
                    }
                    Logger.LogHandledExceptionWithMetadata(
                        message: $"Failed to write config: {Option.RelativePath}",
                        ex: ex,
                        [new("PluginFile", Owner.Location.FilePath)]);
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
                        message: $"Failed to read config: {Option.RelativePath} when reloading, skipping reload.",
                        ex: ex,
                        [new("PluginFile", Owner.Location.FilePath)]);
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
                    message: $"Failed to deserialize config: {Option.RelativePath} when reloading, skipping reload.",
                    ex: ex,
                    [new("PluginFile", Owner.Location.FilePath)]);
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
                        message: $"Failed to read config: {Option.RelativePath} when reloading, skipping reload.",
                        ex: ex,
                        [new("PluginFile", Owner.Location.FilePath)]);
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
                    message: $"Failed to deserialize config: {Option.RelativePath} when reloading, skipping reload.",
                    ex: ex,
                    [new("PluginFile", Owner.Location.FilePath)]);
            }
        }

        public TConfig? Request(bool reloadFromIO = false) {
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

            var config = DeserializeToCache(text, readException);

            if (readException is not null && Option.DeseriAutoPersistFallback) {
                Overwrite(config);
            }

            return config;
        }

        public async Task<TConfig?> RequestAsync(bool reloadFromIO = false, CancellationToken cancellationToken = default) {
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

            var config = DeserializeToCache(text, readException);

            if (readException is not null && Option.DeseriAutoPersistFallback) {
                await OverwriteAsync(config);
            }

            return config;
        }

        public TConfig? TryGetCurrent() => CachedConfig;

        #endregion
    }
}
