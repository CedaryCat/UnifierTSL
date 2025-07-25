using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace UnifierTSL.Logging.Metadata
{
    public ref struct MetadataCollection
    {
        readonly ref MetadataAllocHandle _metadataAllocHandle;
        private Span<KeyValueMetadata> _entries;
        private int _count;

        public MetadataCollection(ref MetadataAllocHandle handle) {
            _metadataAllocHandle = ref handle;
            _entries = _metadataAllocHandle.Allocate(4);
            _count = 0;
        }

        public readonly int Count => _count;
        public readonly bool Supported {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _metadataAllocHandle.IsValid;
        }

        public void Set(string key, string value) {
            if (!Supported) {
                return;
            }
            if (_count >= _entries.Length) {
                var newEntries = _metadataAllocHandle.Allocate(_entries.Length * 2);
                _entries[.._count].CopyTo(newEntries);
                _entries = newEntries;
            }

            int index = BinarySearch(key);
            if (index >= 0) {
                _entries[index] = new KeyValueMetadata(key, value);
                return;
            }

            int insertIndex = ~index; 
            ShiftRight(insertIndex);
            _entries[insertIndex] = new KeyValueMetadata(key, value);
            _count++;
        }


        public readonly bool TryGet(ReadOnlySpan<char> key, [NotNullWhen(true)] out string? value) {
            if (!Supported) {
                value = null;
                return false;
            }
            int index = BinarySearch(key);
            if (index >= 0) {
                value = _entries[index].Value;
                return true;
            }

            value = default;
            return false;
        }

        public readonly ReadOnlySpan<KeyValueMetadata> Metadata => _entries[.._count];

        private readonly void ShiftRight(int index) {
            _entries[index.._count].CopyTo(_entries[(index + 1)..]);
        }


        private readonly int BinarySearch(ReadOnlySpan<char> key) {
            int low = 0, high = _count - 1;
            while (low <= high) {
                int mid = (low + high) / 2;
                int cmp = key.CompareTo(_entries[mid].Key, StringComparison.Ordinal);
                if (cmp == 0) return mid;
                if (cmp < 0) high = mid - 1;
                else low = mid + 1;
            }
            return ~low; 
        }
    }
}
