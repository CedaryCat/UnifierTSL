using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using UnifierTSL.Logging.LogFilters;
using UnifierTSL.Logging.LogWriters;
using UnifierTSL.Logging.Metadata;

namespace UnifierTSL.Logging
{
    public class Logger
    {
        private ILogFilter filter = EmptyLogFilter.Instance;
        public ILogFilter? Filter {
            [return: NotNull]
            get => filter;
            set {
                if (value is null) {
                    filter = EmptyLogFilter.Instance;
                    return;
                }
                filter = value;
            }
        }
        private ILogWriter writer = ConsoleLogWriter.Instance;
        public ILogWriter? Writer {
            [return: NotNull]
            get => writer;
            set {
                if (value is null) {
                    writer = ConsoleLogWriter.Instance;
                    return;
                }
                writer = value;
            }
        }

        public void Log(in LogEntry entry) {
            if (filter.ShouldLog(entry)) {
                writer.Write(entry);
            }
        }

        internal MetadataAllocHandle CreateMetadataAllocHandle() {
            var handle = new InnerMetadataAllocHandle();
            return Unsafe.As<InnerMetadataAllocHandle, MetadataAllocHandle>(ref handle);
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        unsafe struct InnerMetadataAllocHandle()
        {
            public KeyValueMetadata[]? buffer;
            public nint unmanagedData;
            public readonly delegate*<ref InnerMetadataAllocHandle, int, Span<KeyValueMetadata>> allocFunc = cachedAllocFunc;
            public readonly delegate*<ref InnerMetadataAllocHandle, void> freeFunc = cachedFreeFunc;

            public readonly static delegate*<ref InnerMetadataAllocHandle, int, Span<KeyValueMetadata>> cachedAllocFunc = &Allocate;
            public readonly static delegate*<ref InnerMetadataAllocHandle, void> cachedFreeFunc = &Free;


            static Span<KeyValueMetadata> Allocate(ref InnerMetadataAllocHandle handle, int capacity) {
                var buffer = handle.buffer;
                if (buffer is not null) {
                    ArrayPool<KeyValueMetadata>.Shared.Return(buffer);
                }
                buffer = handle.buffer = ArrayPool<KeyValueMetadata>.Shared.Rent(capacity);
                return buffer.AsSpan(0, capacity);
            }
            static void Free(ref InnerMetadataAllocHandle handle) {
                if (handle.buffer is null) {
                    return;
                }
                ArrayPool<KeyValueMetadata>.Shared.Return(handle.buffer);
            }
        }
    }
}
