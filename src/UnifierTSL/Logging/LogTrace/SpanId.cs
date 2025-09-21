using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace UnifierTSL.Logging.LogTrace
{
    [StructLayout(LayoutKind.Explicit, Size = 8)]
    public readonly struct SpanId
    {
        private const int Size = 8;
        private static readonly RandomNumberGenerator r = RandomNumberGenerator.Create();

        [FieldOffset(0)]
        private readonly byte v0;
        [FieldOffset(0)]
        private readonly long data;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe SpanId CreateRandom() {
            Span<byte> memory = stackalloc byte[Size];
            r.GetBytes(memory);
            return Unsafe.As<byte, SpanId>(ref memory[0]);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe SpanId Copy(ActivitySpanId spanId) {
            Span<byte> memory = stackalloc byte[Size];
            spanId.CopyTo(memory);
            return Unsafe.As<byte, SpanId>(ref memory[0]);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void CreateRandom(out SpanId spanId) {
            Unsafe.SkipInit(out spanId);
            Span<byte> memory = MemoryMarshal.CreateSpan(ref Unsafe.As<SpanId, byte>(ref spanId), Size);
            r.GetBytes(memory);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void Copy(ActivitySpanId source, out SpanId destination) {
            Unsafe.SkipInit(out destination);
            Span<byte> memory = MemoryMarshal.CreateSpan(ref Unsafe.As<SpanId, byte>(ref destination), Size);
            source.CopyTo(memory);
        }

        public static unsafe SpanId Parse(ReadOnlySpan<char> hex) {
            if (hex.Length != Size * 2)
                throw new ArgumentException($"SpanId hex string must be {Size * 2} characters.", nameof(hex));

            Span<byte> buffer = stackalloc byte[Size];
            System.Buffers.OperationStatus status = Convert.FromHexString(hex, buffer, out int charsConsumed, out int bytesWritten);
            if (status != System.Buffers.OperationStatus.Done || charsConsumed != hex.Length || bytesWritten != Size)
                throw new FormatException("Invalid hex format for SpanId.");

            return Unsafe.As<byte, SpanId>(ref buffer[0]);
        }

        public static unsafe bool TryParse(ReadOnlySpan<char> hex, out SpanId spanId) {
            if (hex.Length != Size * 2) {
                Unsafe.SkipInit(out spanId);
                return false;
            }

            Span<byte> buffer = stackalloc byte[Size];
            System.Buffers.OperationStatus status = Convert.FromHexString(hex, buffer, out int charsConsumed, out int bytesWritten);
            if (status != System.Buffers.OperationStatus.Done || charsConsumed != hex.Length || bytesWritten != Size) {
                Unsafe.SkipInit(out spanId);
                return false;
            }

            spanId = Unsafe.As<byte, SpanId>(ref buffer[0]);
            return true;
        }

        public static unsafe void ParseOrRandom(ReadOnlySpan<char> hex, out SpanId spanId) {
            if (TryParse(hex, out spanId))
                return;
            CreateRandom(out spanId);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ActivitySpanId ToActivitySpanId() {
            return ActivitySpanId.CreateFromBytes(MemoryMarshal.CreateReadOnlySpan(in v0, Size));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override string ToString() {
            Span<char> buffer = stackalloc char[Size * 2];
            ToHexFormat(buffer);
            return new string(buffer);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string ToHexString() => ToString();

        public void ToHexFormat(Span<char> destination) {
            if (destination.Length < Size * 2) {
                throw new ArgumentException($"SpanId hex string must be {Size * 2} characters.", nameof(destination));
            }
            ref byte byteRef = ref Unsafe.AsRef(in v0);
            ref char charRef = ref destination[0];

            for (int i = 0; i < Size; i++) {
                byte byteData = Unsafe.Add(ref byteRef, i);

                Unsafe.Add(ref charRef, i * 2) = (char)('0' + (byteData >> 4 & 0xF));
                Unsafe.Add(ref charRef, i * 2 + 1) = (char)('0' + (byteData & 0xF));
            }
        }
    }
}
