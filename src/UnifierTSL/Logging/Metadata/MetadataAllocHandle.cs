using System.Buffers;

namespace UnifierTSL.Logging.Metadata
{
    internal struct MetadataAllocHandle
    {
        private KeyValueMetadata[]? buffer;

        public bool TryEnsureCapacity(ref Span<KeyValueMetadata> currentEntries, int usedCount, int requiredCapacity, int maxCapacity) {
            if (requiredCapacity > maxCapacity) {
                return false;
            }

            if (currentEntries.Length >= requiredCapacity) {
                return true;
            }

            int nextCapacity = currentEntries.Length == 0
                ? Math.Max(requiredCapacity, MetadataCollection.InlineCapacity * 2)
                : Math.Max(currentEntries.Length * 2, requiredCapacity);
            nextCapacity = Math.Min(nextCapacity, maxCapacity);
            if (nextCapacity < requiredCapacity) {
                return false;
            }

            KeyValueMetadata[] newBuffer = ArrayPool<KeyValueMetadata>.Shared.Rent(nextCapacity);
            if (!currentEntries.IsEmpty && usedCount > 0) {
                currentEntries[..usedCount].CopyTo(newBuffer.AsSpan(0, usedCount));
            }

            if (buffer is not null) {
                ArrayPool<KeyValueMetadata>.Shared.Return(buffer);
            }

            buffer = newBuffer;
            currentEntries = newBuffer.AsSpan(0, nextCapacity);
            return true;
        }

        public void Free() {
            if (buffer is null) {
                return;
            }

            ArrayPool<KeyValueMetadata>.Shared.Return(buffer);
            buffer = null;
        }
    }
}
