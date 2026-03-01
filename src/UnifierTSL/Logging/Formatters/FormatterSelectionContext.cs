using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;


namespace UnifierTSL.Logging.Formatters
{
    /// <summary>
    /// Represents a collection of available formatter handles for selection.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a <c>ref struct</c> to ensure the underlying formatter handles remain stack-allocated
    /// and are used in a time-local and scope-bound way, preserving correctness of writer state.
    /// </para>
    /// </remarks>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public readonly ref struct FormatterSelectionContext
    {
        /// <summary>
        /// Gets an empty selection context.
        /// </summary>
        public static FormatterSelectionContext Empty => default;

        /// <summary>
        /// The number of formatter handles available in the context.
        /// </summary>
        public readonly int Length;

        private readonly ref InnerHandle datas;

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private readonly struct InnerHandle(nint fuction, ILogWriter writer, IInspectableFormatter formatter)
        {
            public readonly nint fuction = fuction;
            public readonly ILogWriter writer = writer;
            public readonly IInspectableFormatter formatter = formatter;
        }

        /// <summary>
        /// Gets the <see cref="FormatterHandle"/> at the specified index.
        /// </summary>
        /// <param name="index">The zero-based index of the formatter handle.</param>
        /// <returns>A read-only reference to the formatter handle.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the index is outside the valid range.</exception>
        public ref readonly FormatterHandle this[int index] {
            get {
                if (index < 0 || index >= Length) {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }
                return ref Unsafe.As<InnerHandle, FormatterHandle>(ref Unsafe.Add(ref datas, index));
            }
        }
        public Enumerator GetEnumerator() => new(this);
        public ref struct Enumerator(FormatterSelectionContext manager)
        {
            private int index = -1;
            private readonly FormatterSelectionContext manager = manager;
            public readonly ref readonly FormatterHandle Current => ref manager[index];
            public bool MoveNext() {
                index++;
                return index < manager.Length;
            }
        }
    }
}
