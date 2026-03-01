using System.Runtime.InteropServices;


namespace UnifierTSL.Logging.Formatters
{
    /// <summary>
    /// Represents a handle to a formatter that can be switched into active use by the log writer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a <c>ref struct</c> to ensure it remains on the stack and cannot be inadvertently cached
    /// by the caller. This restriction guarantees that the formatter handle reflects the current and valid
    /// state of the log writer, avoiding issues where a previously cached handle may become stale.
    /// </para>
    /// <para>
    /// By using a <c>ref struct</c> rather than an interface, we eliminate the possibility of the
    /// handle escaping to the heap, which could introduce state inconsistency or unintended retention of the writer.
    /// </para>
    /// </remarks>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public readonly ref struct FormatterHandle
    {
        private readonly unsafe delegate*<ILogWriter, IInspectableFormatter, void> fuction;
        private readonly ILogWriter writer;

        /// <summary>
        /// The formatter associated with this handle.
        /// </summary>
        public readonly IInspectableFormatter Formatter;

        /// <summary>
        /// Switches the log writer to use this formatter.
        /// </summary>
        public unsafe void SwitchThisFormatter() {
            fuction(writer, Formatter);
        }
    }
}
