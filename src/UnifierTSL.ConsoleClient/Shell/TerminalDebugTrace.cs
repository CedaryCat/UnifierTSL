#if UNIFIER_TERMINAL_DEBUG_TRACE
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace UnifierTSL.ConsoleClient.Shell
{
    internal static class TerminalDebugTrace
    {
        private sealed class TraceGate
        {
            public long LastTick;
            public int LastHash;
        }

        private static readonly ConcurrentDictionary<string, TraceGate> Gates = new(StringComparer.Ordinal);
        private static readonly bool ScreenBufferDumpEnabled = ResolveScreenBufferDumpEnabled();

        [Conditional("UNIFIER_TERMINAL_DEBUG_TRACE")]
        public static void WriteThrottled(string category, string message, int minIntervalMs = 120) {
            if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(message)) {
                return;
            }

            TraceGate gate = Gates.GetOrAdd(category, static _ => new TraceGate());
            int hash = StringComparer.Ordinal.GetHashCode(message);
            long nowTick = Environment.TickCount64;
            long previousTick = Interlocked.Read(ref gate.LastTick);
            int previousHash = Volatile.Read(ref gate.LastHash);
            if (previousHash == hash && nowTick - previousTick < minIntervalMs) {
                return;
            }

            Volatile.Write(ref gate.LastHash, hash);
            Interlocked.Exchange(ref gate.LastTick, nowTick);
            Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [pid:{Environment.ProcessId}] [{category}] {message}");
        }

        [Conditional("UNIFIER_TERMINAL_DEBUG_TRACE")]
        public static void DumpConsoleSlice(
            string category,
            string headline,
            int topRow,
            int rowCount,
            int minIntervalMs = 120) {
            if (!ScreenBufferDumpEnabled || !OperatingSystem.IsWindows()) {
                return;
            }

            string scopedCategory = $"Screen.{category}";
            string gateMessage = $"{headline}|top={topRow}|rows={rowCount}";
            if (!TryEnterGate(scopedCategory, gateMessage, minIntervalMs)) {
                return;
            }

            if (!TryGetConsoleInfo(out IntPtr outputHandle, out ConsoleScreenBufferInfo info)) {
                Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [pid:{Environment.ProcessId}] [{scopedCategory}] Failed to query console screen buffer.");
                return;
            }

            int bufferWidth = Math.Max(1, (int)info.Size.X);
            int bufferHeight = Math.Max(1, (int)info.Size.Y);
            int cursorTop = info.CursorPosition.Y;
            int cursorLeft = info.CursorPosition.X;
            int windowTop = info.Window.Top;
            int windowBottom = info.Window.Bottom;

            int boundedTop = Math.Clamp(topRow, 0, bufferHeight - 1);
            int boundedRows = Math.Max(1, rowCount);
            int boundedBottom = Math.Clamp(boundedTop + boundedRows - 1, 0, bufferHeight - 1);

            Debug.WriteLine(
                $"[{DateTime.Now:HH:mm:ss.fff}] [pid:{Environment.ProcessId}] [{scopedCategory}] {headline} " +
                $"cursor=({cursorLeft},{cursorTop}) window=[{windowTop}~{windowBottom}] " +
                $"buffer={bufferWidth}x{bufferHeight} dump=[{boundedTop}~{boundedBottom}]");

            for (int row = boundedTop; row <= boundedBottom; row++) {
                string line = ReadConsoleRow(outputHandle, bufferWidth, row);
                string marker = row == cursorTop ? ">" : " ";
                Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [pid:{Environment.ProcessId}] [{scopedCategory}] {marker}{row,4}: {line}");
            }
        }

        private static bool TryEnterGate(string category, string message, int minIntervalMs) {
            TraceGate gate = Gates.GetOrAdd(category, static _ => new TraceGate());
            int hash = StringComparer.Ordinal.GetHashCode(message);
            long nowTick = Environment.TickCount64;
            long previousTick = Interlocked.Read(ref gate.LastTick);
            int previousHash = Volatile.Read(ref gate.LastHash);
            if (previousHash == hash && nowTick - previousTick < minIntervalMs) {
                return false;
            }

            Volatile.Write(ref gate.LastHash, hash);
            Interlocked.Exchange(ref gate.LastTick, nowTick);
            return true;
        }

        private static bool ResolveScreenBufferDumpEnabled() {
            return true;
        }

        private static bool TryGetConsoleInfo(out IntPtr outputHandle, out ConsoleScreenBufferInfo info) {
            outputHandle = GetStdHandle(StdOutputHandle);
            if (outputHandle == IntPtr.Zero || outputHandle == InvalidHandleValue) {
                info = default;
                return false;
            }

            return GetConsoleScreenBufferInfo(outputHandle, out info);
        }

        private static string ReadConsoleRow(IntPtr outputHandle, int width, int row) {
            char[] buffer = new char[width];
            if (!ReadConsoleOutputCharacterW(
                    outputHandle,
                    buffer,
                    (uint)buffer.Length,
                    new Coord(0, (short)row),
                    out uint readChars)) {
                return "<read-failed>";
            }

            int count = (int)Math.Min(readChars, (uint)buffer.Length);
            if (count <= 0) {
                return string.Empty;
            }

            StringBuilder builder = new(count);
            for (int i = 0; i < count; i++) {
                char ch = buffer[i];
                builder.Append(char.IsControl(ch) ? ' ' : ch);
            }

            return builder.ToString().TrimEnd();
        }

        private const int StdOutputHandle = -11;
        private static readonly IntPtr InvalidHandleValue = new(-1);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetConsoleScreenBufferInfo(
            IntPtr hConsoleOutput,
            out ConsoleScreenBufferInfo lpConsoleScreenBufferInfo);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "ReadConsoleOutputCharacterW")]
        private static extern bool ReadConsoleOutputCharacterW(
            IntPtr hConsoleOutput,
            [Out] char[] lpCharacter,
            uint nLength,
            Coord dwReadCoord,
            out uint lpNumberOfCharsRead);

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct Coord(short x, short y)
        {
            public readonly short X = x;
            public readonly short Y = y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct SmallRect(short left, short top, short right, short bottom)
        {
            public readonly short Left = left;
            public readonly short Top = top;
            public readonly short Right = right;
            public readonly short Bottom = bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct ConsoleScreenBufferInfo(
            Coord size,
            Coord cursorPosition,
            short attributes,
            SmallRect window,
            Coord maximumWindowSize)
        {
            public readonly Coord Size = size;
            public readonly Coord CursorPosition = cursorPosition;
            public readonly short Attributes = attributes;
            public readonly SmallRect Window = window;
            public readonly Coord MaximumWindowSize = maximumWindowSize;
        }
    }
}
#endif
