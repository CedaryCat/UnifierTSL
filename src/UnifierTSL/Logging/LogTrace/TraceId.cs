using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace UnifierTSL.Logging.LogTrace
{
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    public readonly struct TraceId {
        const int Size = 16;
        static readonly RandomNumberGenerator r = RandomNumberGenerator.Create();

        [FieldOffset(0)]
        private readonly byte v0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe static TraceId CreateRandom() {
            Span<byte> memory = stackalloc byte[Size];
            r.GetBytes(memory);
            return Unsafe.As<byte, TraceId>(ref memory[0]);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe static TraceId Copy(ActivityTraceId traceId) {
            Span<byte> memory = stackalloc byte[Size];
            traceId.CopyTo(memory);
            return Unsafe.As<byte, TraceId>(ref memory[0]);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe static void CreateRandom(out TraceId traceId) {
            Unsafe.SkipInit(out traceId);
            var memory = MemoryMarshal.CreateSpan(ref Unsafe.As<TraceId, byte>(ref traceId), Size);
            r.GetBytes(memory);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe static void Copy(ActivityTraceId source, out TraceId destination) {
            Unsafe.SkipInit(out destination);
            var memory = MemoryMarshal.CreateSpan(ref Unsafe.As<TraceId, byte>(ref destination), Size);
            source.CopyTo(memory);
        }

        public unsafe static TraceId Parse(ReadOnlySpan<char> hex) {
            if (hex.Length != Size * 2)
                throw new ArgumentException($"TraceId hex string must be {Size * 2} characters.", nameof(hex));

            Span<byte> buffer = stackalloc byte[Size];
            var status = Convert.FromHexString(hex, buffer, out int charsConsumed, out int bytesWritten);
            if (status != System.Buffers.OperationStatus.Done || charsConsumed != hex.Length || bytesWritten != Size)
                throw new FormatException("Invalid hex format for TraceId.");

            return Unsafe.As<byte, TraceId>(ref buffer[0]);
        }

        public unsafe static bool TryParse(ReadOnlySpan<char> hex, out TraceId traceId) {
            if (hex.Length != Size * 2) {
                Unsafe.SkipInit(out traceId);
                return false;
            }

            Span<byte> buffer = stackalloc byte[Size];
            var status = Convert.FromHexString(hex, buffer, out int charsConsumed, out int bytesWritten);
            if (status != System.Buffers.OperationStatus.Done || charsConsumed != hex.Length || bytesWritten != Size) {
                Unsafe.SkipInit(out traceId);
                return false;
            }

            traceId = Unsafe.As<byte, TraceId>(ref buffer[0]);
            return true;
        }

        public unsafe static void ParseOrRandom(ReadOnlySpan<char> hex, out TraceId traceId) {
            if (TryParse(hex, out traceId))
                return;
            CreateRandom(out traceId);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ActivityTraceId ToActivityTraceId() {
            return ActivityTraceId.CreateFromBytes(MemoryMarshal.CreateReadOnlySpan(in v0, Size));
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
                throw new ArgumentException($"TraceId hex string must be {Size * 2} characters.", nameof(destination));
            }
            ref var byteRef = ref Unsafe.AsRef(in v0);
            ref var charRef = ref destination[0];

            for (int i = 0; i < Size; i++) {
                var byteData = Unsafe.Add(ref byteRef, i);

                Unsafe.Add(ref charRef, i * 2) = (char)('0' + (byteData >> 4 & 0xF));
                Unsafe.Add(ref charRef, i * 2 + 1) = (char)('0' + (byteData & 0xF));
            }
        }
    }
}
