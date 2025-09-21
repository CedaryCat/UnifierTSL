using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace UnifierTSL.Logging.LogTrace
{
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    public readonly struct TraceId
    {
        private const int Size = 16;
        private static readonly RandomNumberGenerator r = RandomNumberGenerator.Create();

        [FieldOffset(0)]
        private readonly byte v0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe TraceId CreateRandom() {
            Span<byte> memory = stackalloc byte[Size];
            r.GetBytes(memory);
            return Unsafe.As<byte, TraceId>(ref memory[0]);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe TraceId Copy(ActivityTraceId traceId) {
            Span<byte> memory = stackalloc byte[Size];
            traceId.CopyTo(memory);
            return Unsafe.As<byte, TraceId>(ref memory[0]);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void CreateRandom(out TraceId traceId) {
            Unsafe.SkipInit(out traceId);
            Span<byte> memory = MemoryMarshal.CreateSpan(ref Unsafe.As<TraceId, byte>(ref traceId), Size);
            r.GetBytes(memory);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void Copy(ActivityTraceId source, out TraceId destination) {
            Unsafe.SkipInit(out destination);
            Span<byte> memory = MemoryMarshal.CreateSpan(ref Unsafe.As<TraceId, byte>(ref destination), Size);
            source.CopyTo(memory);
        }

        public static unsafe TraceId Parse(ReadOnlySpan<char> hex) {
            if (hex.Length != Size * 2)
                throw new ArgumentException($"TraceId hex string must be {Size * 2} characters.", nameof(hex));

            Span<byte> buffer = stackalloc byte[Size];
            System.Buffers.OperationStatus status = Convert.FromHexString(hex, buffer, out int charsConsumed, out int bytesWritten);
            if (status != System.Buffers.OperationStatus.Done || charsConsumed != hex.Length || bytesWritten != Size)
                throw new FormatException("Invalid hex format for TraceId.");

            return Unsafe.As<byte, TraceId>(ref buffer[0]);
        }

        public static unsafe bool TryParse(ReadOnlySpan<char> hex, out TraceId traceId) {
            if (hex.Length != Size * 2) {
                Unsafe.SkipInit(out traceId);
                return false;
            }

            Span<byte> buffer = stackalloc byte[Size];
            System.Buffers.OperationStatus status = Convert.FromHexString(hex, buffer, out int charsConsumed, out int bytesWritten);
            if (status != System.Buffers.OperationStatus.Done || charsConsumed != hex.Length || bytesWritten != Size) {
                Unsafe.SkipInit(out traceId);
                return false;
            }

            traceId = Unsafe.As<byte, TraceId>(ref buffer[0]);
            return true;
        }

        public static unsafe void ParseOrRandom(ReadOnlySpan<char> hex, out TraceId traceId) {
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
