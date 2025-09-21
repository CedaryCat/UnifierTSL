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

            MetadataAllocHandle metadataAllocHandle = Logger.CreateMetadataAllocHandle();
            try {
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
                    ref metadataAllocHandle
                );
                if (_injectors.Length > 0) {
                    foreach (ILogMetadataInjector injector in _injectors) {
                        injector.InjectMetadata(ref logEntry);
                    }
                }
                logger.Log(ref logEntry);
            }
            finally {
                metadataAllocHandle.Free();
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

            MetadataAllocHandle metadataAllocHandle = Logger.CreateMetadataAllocHandle();
            try {
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
                    ref metadataAllocHandle
                );

                int metadataLen = metadata.Length;
                if (metadataLen > 0) {
                    ref KeyValueMetadata e0 = ref MemoryMarshal.GetReference(metadata);
                    for (int i = 0; i < metadata.Length; i++) {
                        KeyValueMetadata element = Unsafe.Add(ref e0, i);
                        logEntry.SetMetadata(element.Key, element.Value);
                    }
                }
                if (_injectors.Length > 0) {
                    foreach (ILogMetadataInjector injector in _injectors) {
                        injector.InjectMetadata(ref logEntry);
                    }
                }
                logger.Log(ref logEntry);
            }
            finally {
                metadataAllocHandle.Free();
            }
        }
        /// <summary>
        /// Emits a log message with the provided level, message, and correlation context information.
        /// </summary>
        /// <param name="level">The severity level of the log message.</param>
        /// <param name="message">The log message text.</param>
        /// <param name="traceContext">The correlation context information.</param>
        /// <param name="overwriteCategory">An optional override for the default category.</param>
        /// <param name="exception">An optional exception to include in the log.</param>
        /// <param name="eventId">An optional event identifier.</param>
        /// <param name="sourceFilePath">The path of the source file that emitted the log (auto-filled).</param>
        /// <param name="memberName">The name of the member emitting the log (auto-filled).</param>
        /// <param name="sourceLineNumber">The source line number where the log was emitted (auto-filled).</param>
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

            MetadataAllocHandle metadataAllocHandle = Logger.CreateMetadataAllocHandle();
            try {
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
                    ref metadataAllocHandle
                );
                if (_injectors.Length > 0) {
                    foreach (ILogMetadataInjector injector in _injectors) {
                        injector.InjectMetadata(ref logEntry);
                    }
                }
                logger.Log(ref logEntry);
            }
            finally {
                metadataAllocHandle.Free();
            }
        }
        /// <summary>
        /// Emits a log message with the provided level, message, correlation context information, and custom metadata.
        /// </summary>
        /// <param name="level">The severity level of the log message.</param>
        /// <param name="message">The log message text.</param>
        /// <param name="traceContext">The correlation context information.</param>
        /// <param name="metadata">The custom metadata to include in the log.</param>
        /// <param name="overwriteCategory">An optional override for the default category.</param>
        /// <param name="exception">An optional exception to include in the log.</param>
        /// <param name="eventId">An optional event identifier.</param>
        /// <param name="sourceFilePath">The path of the source file that emitted the log (auto-filled).</param>
        /// <param name="memberName">The name of the member emitting the log (auto-filled).</param>
        /// <param name="sourceLineNumber">The source line number where the log was emitted (auto-filled).</param>
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
            MetadataAllocHandle metadataAllocHandle = Logger.CreateMetadataAllocHandle();
            try {
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
                    ref metadataAllocHandle
                );

                int metadataLen = metadata.Length;
                if (metadataLen > 0) {
                    ref KeyValueMetadata e0 = ref MemoryMarshal.GetReference(metadata);
                    for (int i = 0; i < metadata.Length; i++) {
                        KeyValueMetadata element = Unsafe.Add(ref e0, i);
                        logEntry.SetMetadata(element.Key, element.Value);
                    }
                }
                if (_injectors.Length > 0) {
                    foreach (ILogMetadataInjector injector in _injectors) {
                        injector.InjectMetadata(ref logEntry);
                    }
                }
                logger.Log(ref logEntry);
            }
            finally {
                metadataAllocHandle.Free();
            }
        }
    }
}
