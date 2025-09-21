using System.Buffers;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using UnifierTSL.Logging.LogFilters;
using UnifierTSL.Logging.LogWriters;
using UnifierTSL.Logging.Metadata;

namespace UnifierTSL.Logging
{
    public class Logger : IMetadataInjectionHost
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

        private ImmutableArray<ILogMetadataInjector> _injectors = ImmutableArray<ILogMetadataInjector>.Empty;
        public IReadOnlyList<ILogMetadataInjector> MetadataInjectors => _injectors;

        public void AddMetadataInjector(ILogMetadataInjector injector) {
            ArgumentNullException.ThrowIfNull(injector);
            ImmutableInterlocked.Update(ref _injectors, arr => arr.Contains(injector) ? arr : arr.Add(injector));
        }

        public void RemoveMetadataInjector(ILogMetadataInjector injector) {
            ArgumentNullException.ThrowIfNull(injector);
            ImmutableInterlocked.Update(ref _injectors, arr => arr.Remove(injector));
        }

        public void Log(ref LogEntry entry) {
            ReadOnlySpan<ILogMetadataInjector> injectors = _injectors.AsSpan();
            int injectorCount = injectors.Length;
            if (injectorCount > 0) {
                ref ILogMetadataInjector element0 = ref MemoryMarshal.GetReference(injectors);
                for (int i = 0; i < injectorCount; i++) {
                    Unsafe.Add(ref element0, i).InjectMetadata(ref entry);
                }
            }

            if (filter.ShouldLog(in entry)) {
                writer.Write(in entry);
            }
        }

        internal static MetadataAllocHandle CreateMetadataAllocHandle() {
            InnerMetadataAllocHandle handle = new();
            return Unsafe.As<InnerMetadataAllocHandle, MetadataAllocHandle>(ref handle);
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private unsafe struct InnerMetadataAllocHandle()
        {
            public KeyValueMetadata[]? buffer;
            public nint unmanagedData;
            public readonly delegate*<ref InnerMetadataAllocHandle, int, Span<KeyValueMetadata>> allocFunc = cachedAllocFunc;
            public readonly delegate*<ref InnerMetadataAllocHandle, void> freeFunc = cachedFreeFunc;

            public static readonly delegate*<ref InnerMetadataAllocHandle, int, Span<KeyValueMetadata>> cachedAllocFunc = &Allocate;
            public static readonly delegate*<ref InnerMetadataAllocHandle, void> cachedFreeFunc = &Free;


            private static Span<KeyValueMetadata> Allocate(ref InnerMetadataAllocHandle handle, int capacity) {
                KeyValueMetadata[]? buffer = handle.buffer;
                if (buffer is not null) {
                    ArrayPool<KeyValueMetadata>.Shared.Return(buffer);
                }
                buffer = handle.buffer = ArrayPool<KeyValueMetadata>.Shared.Rent(capacity);
                return buffer.AsSpan(0, capacity);
            }
            private static void Free(ref InnerMetadataAllocHandle handle) {
                if (handle.buffer is null) {
                    return;
                }
                ArrayPool<KeyValueMetadata>.Shared.Return(handle.buffer);
            }
        }
    }
}
