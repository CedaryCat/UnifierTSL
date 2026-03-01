using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using UnifierTSL.Logging.LogTrace;
using UnifierTSL.Logging.Metadata;

namespace UnifierTSL.Logging
{
    public class RoleLogger : IMetadataInjectionHost, IStandardLogger
    {
        private ImmutableArray<ILogMetadataInjector> _injectors = ImmutableArray<ILogMetadataInjector>.Empty;
        public IReadOnlyList<ILogMetadataInjector> MetadataInjectors => _injectors;

        public void AddMetadataInjector(ILogMetadataInjector injector) {
            ArgumentNullException.ThrowIfNull(injector);
            ImmutableInterlocked.Update(ref _injectors, arr => arr.Add(injector));
        }

        public void RemoveMetadataInjector(ILogMetadataInjector injector) {
            ArgumentNullException.ThrowIfNull(injector);
            ImmutableInterlocked.Update(ref _injectors, arr => arr.Remove(injector));
        }

        private readonly Logger logger;
        private readonly ILoggerHost role;

        internal RoleLogger(ILoggerHost host, Logger logger) {
            this.logger = logger;
            role = host;
        }

        public RoleLogger CloneForHost(ILoggerHost host, bool inheritInjectors = false) {
            RoleLogger roleLogger = new(host, logger);
            if (inheritInjectors) {
                foreach (ILogMetadataInjector injector in _injectors) {
                    roleLogger.AddMetadataInjector(injector);
                }
            }
            return roleLogger;
        }
        public RoleLogger Clone(bool inheritInjectors = false) => CloneForHost(role, inheritInjectors);

        public void Log(
            LogLevel level,
            string message,
            string? overwriteCategory = null,
            Exception? exception = null,
            int eventId = default,
            [CallerFilePath] string? sourceFilePath = null,
            [CallerMemberName] string? memberName = null,
            [CallerLineNumber] int? sourceLineNumber = null) {

            LogEntry logEntry = new(
                timestampUtc: DateTimeOffset.UtcNow,
                level: level,
                eventId: eventId,
                role: role.Name,
                category: overwriteCategory ?? role.CurrentLogCategory ?? "",
                message: message,
                exception: exception,
                sourceFilePath: sourceFilePath,
                memberName: memberName,
                sourceLineNumber: sourceLineNumber,
                supportsMetadata: true
            );

            try {
                if (_injectors.Length > 0) {
                    foreach (ILogMetadataInjector injector in _injectors) {
                        injector.InjectMetadata(ref logEntry);
                    }
                }
                logger.Log(ref logEntry);
            }
            finally {
                logEntry.ReleaseMetadataResources();
            }
        }

        public void Log(
            LogLevel level,
            string message,
            ReadOnlySpan<KeyValueMetadata> metadata,
            string? overwriteCategory = null,
            Exception? exception = null,
            int eventId = default,
            [CallerFilePath] string? sourceFilePath = null,
            [CallerMemberName] string? memberName = null,
            [CallerLineNumber] int? sourceLineNumber = null) {

            LogEntry logEntry = new(
                timestampUtc: DateTimeOffset.UtcNow,
                level: level,
                eventId: eventId,
                role: role.Name,
                category: overwriteCategory ?? role.CurrentLogCategory ?? "",
                message: message,
                exception: exception,
                sourceFilePath: sourceFilePath,
                memberName: memberName,
                sourceLineNumber: sourceLineNumber,
                supportsMetadata: true
            );

            try {
                CopyMetadata(metadata, ref logEntry);
                if (_injectors.Length > 0) {
                    foreach (ILogMetadataInjector injector in _injectors) {
                        injector.InjectMetadata(ref logEntry);
                    }
                }
                logger.Log(ref logEntry);
            }
            finally {
                logEntry.ReleaseMetadataResources();
            }
        }

        /// <summary>
        /// Emits a log message with the provided level, message, and correlation context information.
        /// </summary>
        public void Log(
            LogLevel level,
            string message,
            in TraceContext traceContext,
            string? overwriteCategory = null,
            Exception? exception = null,
            int eventId = default,
            [CallerFilePath] string? sourceFilePath = null,
            [CallerMemberName] string? memberName = null,
            [CallerLineNumber] int? sourceLineNumber = null) {

            LogEntry logEntry = new(
                timestampUtc: DateTimeOffset.UtcNow,
                level: level,
                eventId: eventId,
                role: role.Name,
                category: overwriteCategory ?? role.CurrentLogCategory ?? "",
                message: message,
                traceContext: traceContext,
                exception: exception,
                sourceFilePath: sourceFilePath,
                memberName: memberName,
                sourceLineNumber: sourceLineNumber,
                supportsMetadata: true
            );

            try {
                if (_injectors.Length > 0) {
                    foreach (ILogMetadataInjector injector in _injectors) {
                        injector.InjectMetadata(ref logEntry);
                    }
                }
                logger.Log(ref logEntry);
            }
            finally {
                logEntry.ReleaseMetadataResources();
            }
        }

        /// <summary>
        /// Emits a log message with the provided level, message, correlation context information, and custom metadata.
        /// </summary>
        public void Log(
            LogLevel level,
            string message,
            in TraceContext traceContext,
            ReadOnlySpan<KeyValueMetadata> metadata,
            string? overwriteCategory = null,
            Exception? exception = null,
            int eventId = default,
            [CallerFilePath] string? sourceFilePath = null,
            [CallerMemberName] string? memberName = null,
            [CallerLineNumber] int? sourceLineNumber = null) {

            LogEntry logEntry = new(
                timestampUtc: DateTimeOffset.UtcNow,
                level: level,
                eventId: eventId,
                role: role.Name,
                category: overwriteCategory ?? role.CurrentLogCategory ?? "",
                message: message,
                traceContext: traceContext,
                exception: exception,
                sourceFilePath: sourceFilePath,
                memberName: memberName,
                sourceLineNumber: sourceLineNumber,
                supportsMetadata: true
            );

            try {
                CopyMetadata(metadata, ref logEntry);
                if (_injectors.Length > 0) {
                    foreach (ILogMetadataInjector injector in _injectors) {
                        injector.InjectMetadata(ref logEntry);
                    }
                }
                logger.Log(ref logEntry);
            }
            finally {
                logEntry.ReleaseMetadataResources();
            }
        }

        private static void CopyMetadata(ReadOnlySpan<KeyValueMetadata> metadata, ref LogEntry logEntry) {
            int metadataLen = metadata.Length;
            if (metadataLen <= 0) {
                return;
            }

            ref KeyValueMetadata e0 = ref MemoryMarshal.GetReference(metadata);
            for (int i = 0; i < metadataLen; i++) {
                KeyValueMetadata element = Unsafe.Add(ref e0, i);
                logEntry.SetMetadata(element.Key, element.Value);
            }
        }
    }
}
