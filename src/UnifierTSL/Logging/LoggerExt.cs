using System;
using System.Runtime.CompilerServices;
using UnifierTSL.Logging;
using UnifierTSL.Logging.Metadata;

public static class LoggerExt
{
    public static void Trace<TLogger>(this TLogger logger, string message,
        string? category = null,
        Exception? ex = null,
        int eventId = 0,
        [CallerFilePath] string? file = null,
        [CallerMemberName] string? member = null,
        [CallerLineNumber] int line = 0)
        where TLogger : IStandardLogger =>
        logger.Log(LogLevel.Trace, message, category, ex, eventId, file, member, line);

    public static void Debug<TLogger>(this TLogger logger, string message,
        string? category = null,
        Exception? ex = null,
        int eventId = 0,
        [CallerFilePath] string? file = null,
        [CallerMemberName] string? member = null,
        [CallerLineNumber] int line = 0)
        where TLogger : IStandardLogger =>
        logger.Log(LogLevel.Debug, message, category, ex, eventId, file, member, line);

    public static void Info<TLogger>(this TLogger logger, string message,
        string? category = null,
        Exception? ex = null,
        int eventId = 0,
        [CallerFilePath] string? file = null,
        [CallerMemberName] string? member = null,
        [CallerLineNumber] int line = 0)
        where TLogger : IStandardLogger =>
        logger.Log(LogLevel.Info, message, category, ex, eventId, file, member, line);

    public static void Success<TLogger>(this TLogger logger, string message,
        string? category = null,
        Exception? ex = null,
        int eventId = 0,
        [CallerFilePath] string? file = null,
        [CallerMemberName] string? member = null,
        [CallerLineNumber] int line = 0)
        where TLogger : IStandardLogger =>
        logger.Log(LogLevel.Success, message, category, ex, eventId, file, member, line);

    public static void Warning<TLogger>(this TLogger logger, string message,
        string? category = null,
        Exception? ex = null,
        int eventId = 0,
        [CallerFilePath] string? file = null,
        [CallerMemberName] string? member = null,
        [CallerLineNumber] int line = 0)
        where TLogger : IStandardLogger =>
        logger.Log(LogLevel.Warning, message, category, ex, eventId, file, member, line);

    public static void Error<TLogger>(this TLogger logger, string message,
        Exception? ex = null,
        string? category = null,
        int eventId = 0,
        [CallerFilePath] string? file = null,
        [CallerMemberName] string? member = null,
        [CallerLineNumber] int line = 0)
        where TLogger : IStandardLogger =>
        logger.Log(LogLevel.Error, message, category, ex, eventId, file, member, line);

    public static void Critical<TLogger>(this TLogger logger, string message,
        Exception? ex = null,
        string? category = null,
        int eventId = 0,
        [CallerFilePath] string? file = null,
        [CallerMemberName] string? member = null,
        [CallerLineNumber] int line = 0)
        where TLogger : IStandardLogger =>
        logger.Log(LogLevel.Critical, message, category, ex, eventId, file, member, line);

    public static void InfoWithMetadata<TLogger>(this TLogger logger, string message,
        ReadOnlySpan<KeyValueMetadata> metadata,
        string? category = null,
        Exception? ex = null,
        int eventId = 0,
        [CallerFilePath] string? file = null,
        [CallerMemberName] string? member = null,
        [CallerLineNumber] int line = 0)
        where TLogger : IStandardLogger =>
        logger.Log(LogLevel.Info, message, metadata, category, ex, eventId, file, member, line);

    public static void LogWithMetadata<TLogger>(this TLogger logger, LogLevel level,
        string message,
        ReadOnlySpan<KeyValueMetadata> metadata,
        string? category = null,
        Exception? ex = null,
        int eventId = 0,
        [CallerFilePath] string? file = null,
        [CallerMemberName] string? member = null,
        [CallerLineNumber] int line = 0)
        where TLogger : IStandardLogger =>
        logger.Log(level, message, metadata, category, ex, eventId, file, member, line);

    public static void LogHandledException<TLogger>(this TLogger logger, string message,
        Exception ex,
        LogLevel level = LogLevel.Warning,
        string? category = null,
        int eventId = 0,
        [CallerFilePath] string? file = null,
        [CallerMemberName] string? member = null,
        [CallerLineNumber] int line = 0)
        where TLogger : IStandardLogger =>
        logger.Log(level, message, category, ex, eventId, file, member, line);

    public static void LogUnexpectedException<TLogger>(this TLogger logger, string message,
        Exception ex,
        LogLevel level = LogLevel.Error,
        string? category = null,
        int eventId = 0,
        [CallerFilePath] string? file = null,
        [CallerMemberName] string? member = null,
        [CallerLineNumber] int line = 0)
        where TLogger : IStandardLogger =>
        logger.Log(level, message, category, ex, eventId, file, member, line); 
    
    public static void LogHandledExceptionWithMetadata<TLogger>(this TLogger logger, string message,
        Exception ex,
        ReadOnlySpan<KeyValueMetadata> metadata,
        LogLevel level = LogLevel.Warning,
        string? category = null,
        int eventId = 0,
        [CallerFilePath] string? file = null,
        [CallerMemberName] string? member = null,
        [CallerLineNumber] int line = 0)
        where TLogger : IStandardLogger =>
        logger.Log(level, message, metadata, category, ex, eventId, file, member, line);

    public static void LogUnexpectedExceptionWithMetadata<TLogger>(this TLogger logger, string message,
        Exception ex,
        ReadOnlySpan<KeyValueMetadata> metadata,
        LogLevel level = LogLevel.Error,
        string? category = null,
        int eventId = 0,
        [CallerFilePath] string? file = null,
        [CallerMemberName] string? member = null,
        [CallerLineNumber] int line = 0)
        where TLogger : IStandardLogger =>
        logger.Log(level, message, metadata, category, ex, eventId, file, member, line);
}

