using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace UnifierTSL.Logging.Metadata
{
    public ref struct MetadataCollection
    {
        public const int InlineCapacity = 8;
        public const int MaxCapacity = 32;

        [InlineArray(InlineCapacity)]
        private struct InlineMetadataChunk
        {
            private KeyValueMetadata element0;
        }

        private MetadataAllocHandle growthHandle;
        private Span<KeyValueMetadata> overflowEntries;
        private int count;
        private bool overflowed;
        private InlineMetadataChunk inlineEntries;

        internal MetadataCollection(bool supported) {
            growthHandle = default;
            overflowEntries = default;
            count = supported ? 0 : -1;
            overflowed = false;
            inlineEntries = default;
        }

        public readonly int Count => count < 0 ? 0 : count;
        public readonly bool Overflowed => overflowed;

        public void Set(string key, string value) {
            if (count < 0) {
                return;
            }

            KeyValueMetadata entry = new(key, value);
            if (TryFindIndex(key, out int existingIndex)) {
                SetAt(existingIndex, entry);
                return;
            }

            if (overflowed || count >= MaxCapacity) {
                overflowed = true;
                return;
            }

            if (!EnsureStorageForAppend()) {
                overflowed = true;
                return;
            }

            SetAt(count, entry);
            count++;
        }

        public bool TryGet(ReadOnlySpan<char> key, [NotNullWhen(true)] out string? value) {
            int entryCount = count;
            if (entryCount <= 0) {
                value = null;
                return false;
            }

            ref KeyValueMetadata baseRef = ref GetBaseRef(ref this);
            for (int i = 0; i < entryCount; i++) {
                ref readonly KeyValueMetadata entry = ref Unsafe.Add(ref baseRef, i);
                if (key.Equals(entry.Key, StringComparison.Ordinal)) {
                    value = entry.Value;
                    return true;
                }
            }

            value = null;
            return false;
        }

        public KeyValueMetadata GetAt(int index) {
            int entryCount = count;
            if ((uint)index >= (uint)entryCount) {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            ref KeyValueMetadata baseRef = ref GetBaseRef(ref this);
            return Unsafe.Add(ref baseRef, index);
        }

        public void Dispose() {
            growthHandle.Free();
            overflowEntries = default;
            count = -1;
            overflowed = false;
        }

        private bool EnsureStorageForAppend() {
            if (overflowEntries.IsEmpty && count < InlineCapacity) {
                return true;
            }

            Span<KeyValueMetadata> currentEntries = GetCurrentStorageSpan();
            if (!growthHandle.TryEnsureCapacity(ref currentEntries, count, count + 1, MaxCapacity)) {
                return false;
            }

            overflowEntries = currentEntries;
            return true;
        }

        private bool TryFindIndex(ReadOnlySpan<char> key, out int index) {
            int entryCount = count;
            if (entryCount <= 0) {
                index = -1;
                return false;
            }

            ref KeyValueMetadata baseRef = ref GetBaseRef(ref this);
            for (int i = 0; i < entryCount; i++) {
                if (key.Equals(Unsafe.Add(ref baseRef, i).Key, StringComparison.Ordinal)) {
                    index = i;
                    return true;
                }
            }

            index = -1;
            return false;
        }

        private void SetAt(int index, KeyValueMetadata entry) {
            ref KeyValueMetadata baseRef = ref GetBaseRef(ref this);
            Unsafe.Add(ref baseRef, index) = entry;
        }

        private Span<KeyValueMetadata> GetCurrentStorageSpan() {
            if (!overflowEntries.IsEmpty) {
                return overflowEntries;
            }

            ref KeyValueMetadata baseRef = ref inlineEntries[0];
            return MemoryMarshal.CreateSpan(ref baseRef, InlineCapacity);
        }

        private static ref KeyValueMetadata GetBaseRef(ref MetadataCollection metadata) {
            if (!metadata.overflowEntries.IsEmpty) {
                return ref MemoryMarshal.GetReference(metadata.overflowEntries);
            }

            return ref metadata.inlineEntries[0];
        }
    }
}
