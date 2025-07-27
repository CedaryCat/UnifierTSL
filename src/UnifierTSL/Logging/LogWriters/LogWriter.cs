using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using UnifierTSL.Commons;
using UnifierTSL.Logging.Formatters;

namespace UnifierTSL.Logging.LogWriters
{
    /// <summary>
    /// Provides a simplified base class for log writers that store raw or structured log entries
    /// without requiring any formatter selection logic.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This implementation is intended for log writers that persist or process log entries in their
    /// original, unformatted structure—such as binary log stores, structured JSON sinks, or audit pipelines.
    /// </para>
    /// <para>
    /// Because formatting is unnecessary in such scenarios, the <see cref="GetAvailableFormatters"/> method
    /// always returns an empty <see cref="FormatterSelectionContext"/>.
    /// </para>
    /// </remarks>
    public abstract class LogWriter : ILogWriter
    {
        /// <summary>
        /// Gets the available formatters. For this implementation, always returns an empty context.
        /// </summary>
        /// <param name="availableFormatters">The output formatter selection context, which is always empty.</param>
        public void GetAvailableFormatters(out FormatterSelectionContext availableFormatters)
            => availableFormatters = FormatterSelectionContext.Empty;

        /// <summary>
        /// Writes the specified log entry using the concrete implementation.
        /// </summary>
        /// <param name="log">The log entry to write.</param>
        public abstract void Write(scoped in LogEntry log);
    }

    /// <summary>
    /// Represents an abstract base class for log writers that use a specific input type.
    /// </summary>
    /// <typeparam name="TInput">The type of input data to be written, which must be non-nullable.</typeparam>
    public abstract class LogWriter<TInput> : ILogWriter where TInput : notnull
    {
        readonly ILogFormatter<TInput> defFormatter;
        ILogFormatter<TInput> curFormatter;
        readonly HashSet<ILogFormatter<TInput>> availableFormatters = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="LogWriter{TInput}"/> class with a default formatter.
        /// </summary>
        /// <param name="defaultFormatter">The default formatter to use when no other formatter is specified.</param>
        /// <exception cref="ArgumentNullException">Thrown if the <paramref name="defaultFormatter"/> is null.</exception>
        public LogWriter(ILogFormatter<TInput> defaultFormatter) {
            TSLThrowHelper.ThrowIfNull(defaultFormatter);
            curFormatter = defFormatter = defaultFormatter;
            availableFormatters.Add(defaultFormatter);
        }

        /// <summary>
        /// Gets or sets the current formatter used for formatting log entries.
        /// <para>
        /// If a <c>null</c> value is assigned, the formatter will fall back to the default formatter
        /// provided at construction. If a non-null formatter is assigned, it will automatically be
        /// added to the list of available formatters.
        /// </para>
        /// <para>
        /// This property is guaranteed to never return <c>null</c>. It will always return either the
        /// explicitly assigned formatter or the non-null default formatter.
        /// </para>
        /// </summary>
        public ILogFormatter<TInput>? Formatter {
            [return: NotNull]
            get => curFormatter;
            set {
                curFormatter = value ?? defFormatter;
                if (value != null)
                    availableFormatters.Add(value);
            }
        }

        /// <summary>
        /// Manually adds a formatter to the list of available formatters.
        /// </summary>
        /// <param name="formatter">The formatter to add.</param>
        /// <returns><c>true</c> if the formatter was added; <c>false</c> if it was already present.</returns>
        public bool AddFormatter(ILogFormatter<TInput> formatter) {
            return availableFormatters.Add(formatter);
        }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private readonly ref struct InnerSelectionContext(ref InnerSelectionContext.InnerHandle element0, int length)
        {
            readonly int Length = length;
            readonly ref InnerHandle datas = ref element0;
            [StructLayout(LayoutKind.Sequential, Pack = 1)]
            public readonly struct InnerHandle(nint fuction, LogWriter<TInput> writer, ILogFormatter<TInput> formatter)
            {
                readonly nint fuction = fuction;
                readonly LogWriter<TInput> writer = writer;
                readonly ILogFormatter<TInput> formatter = formatter;
            }
        }
        static void SwitchFormatter(LogWriter<TInput> writer, ILogFormatter<TInput> formatter) {
            if (formatter is null) {
                return;
            }
            if (writer.availableFormatters.Contains(formatter)) {
                writer.curFormatter = formatter;
            }
        }
        public unsafe void GetAvailableFormatters(out FormatterSelectionContext availableFormatters) {
            delegate*<LogWriter<TInput>, ILogFormatter<TInput>, void> fptr = &SwitchFormatter;
            var snapshot = this.availableFormatters.Select(x => new InnerSelectionContext.InnerHandle((nint)fptr, this, x)).ToArray();
            var context = new InnerSelectionContext(ref snapshot[0], snapshot.Length);
            availableFormatters = Unsafe.As<InnerSelectionContext, FormatterSelectionContext>(ref context);
        }
        /// <summary>
        /// Writes the specified input using the concrete implementation.
        /// </summary>
        /// <param name="input">The input data to write.</param>
        public abstract void Write(Span<TInput> input);

        /// <summary>
        /// Formats a <see cref="LogEntry"/> using the current formatter and writes the formatted output.
        /// </summary>
        /// <param name="log">The log entry to write.</param>
        public void Write(scoped in LogEntry log) {
            int bufferSize;
            Span<TInput> formatted;
            TInput[]? buffer = null;
            try {
                bufferSize = curFormatter.GetEstimatedSize(log);
                buffer = ArrayPool<TInput>.Shared.Rent(bufferSize);
                curFormatter.Format(in log, ref buffer[0], out var written);
                formatted = buffer.AsSpan(0, written);
            }
            catch (Exception ex) {
                if (buffer is not null) {
                    ArrayPool<TInput>.Shared.Return(buffer);
                }
                LogFactory.CreateException(
                    role: "logCore",
                    category: "FormatterFailure",
                    message: $"Failed to format log entry [TimestampUtc:{log.TimestampUtc:u}] using formatter '{curFormatter.FormatName}'. " +
                             $"Original log entry has been re-formatted using the default formatter.",
                    exception: ex,
                    out var errorLog);

                bufferSize = defFormatter.GetEstimatedSize(errorLog);
                buffer = ArrayPool<TInput>.Shared.Rent(bufferSize);
                defFormatter.Format(in errorLog, ref buffer[0], out var written);
                formatted = buffer.AsSpan(0, written);
                Write(formatted);
                ArrayPool<TInput>.Shared.Return(buffer);

                bufferSize = defFormatter.GetEstimatedSize(log);
                buffer = ArrayPool<TInput>.Shared.Rent(bufferSize);
                defFormatter.Format(in log, ref buffer[0], out written);
                formatted = buffer.AsSpan(0, written);
            }
            Write(formatted);
            ArrayPool<TInput>.Shared.Return(buffer);
            return;
        }
    }
}
