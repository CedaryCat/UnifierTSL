using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace UnifierTSL.Logging.Metadata
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public readonly unsafe struct MetadataAllocHandle {
        readonly object? managedData;
        readonly nint unmanagedData;
        readonly delegate*<MetadataAllocHandle, int, Span<KeyValueMetadata>> allocFunc;
        readonly delegate*<MetadataAllocHandle, void> freeFunc;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<KeyValueMetadata> Allocate(int capacity) => allocFunc(this, capacity);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Free() => freeFunc(this);
        public bool IsValid {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => allocFunc is not null && freeFunc is not null;
        }
    }
}
