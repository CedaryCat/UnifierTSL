using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace UnifierTSL.Logging.Metadata
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal unsafe struct MetadataAllocHandle
    {
        // [SuppressMessage("Style", "IDE0044:Add readonly modifier", Justification = "Modified via unsafe code.")]
        private object? managedData;
        private nint unmanagedData;

        private readonly delegate*<ref MetadataAllocHandle, int, Span<KeyValueMetadata>> allocFunc;
        private readonly delegate*<ref MetadataAllocHandle, void> freeFunc;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<KeyValueMetadata> Allocate(int capacity) => allocFunc(ref Unsafe.AsRef(ref this), capacity);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Free() => freeFunc(ref this);
        public readonly bool IsValid {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => allocFunc is not null && freeFunc is not null;
        }
    }
}
