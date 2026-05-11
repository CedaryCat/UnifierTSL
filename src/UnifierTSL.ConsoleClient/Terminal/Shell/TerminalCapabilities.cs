using System.Runtime.InteropServices;

namespace UnifierTSL.Terminal.Shell {
    public sealed class TerminalCapabilities {
        private const int StdOutputHandle = -11;
        private const uint EnableVirtualTerminalProcessing = 0x0004;

        public bool IsInteractive { get; }

        public bool SupportsVirtualTerminal { get; }

        public bool IsInputRedirected { get; }

        public bool IsOutputRedirected { get; }

        private TerminalCapabilities(bool isInteractive, bool supportsVirtualTerminal, bool isInputRedirected, bool isOutputRedirected) {
            IsInteractive = isInteractive;
            SupportsVirtualTerminal = supportsVirtualTerminal;
            IsInputRedirected = isInputRedirected;
            IsOutputRedirected = isOutputRedirected;
        }

        public static TerminalCapabilities Detect() {
            bool inputRedirected;
            bool outputRedirected;
            try {
                inputRedirected = Console.IsInputRedirected;
                outputRedirected = Console.IsOutputRedirected;
            }
            catch {
                inputRedirected = true;
                outputRedirected = true;
            }

            bool interactive = !inputRedirected && !outputRedirected && CanMoveCursor();

            bool supportsVt;
            if (outputRedirected) {
                supportsVt = false;
            }
            else if (OperatingSystem.IsWindows()) {
                supportsVt = TryEnableWindowsVt();
            }
            else {
                supportsVt = true;
            }

            return new TerminalCapabilities(interactive, supportsVt, inputRedirected, outputRedirected);
        }

        private static bool CanMoveCursor() {
            try {
                int left = Console.CursorLeft;
                int top = Console.CursorTop;
                Console.SetCursorPosition(left, top);
                return true;
            }
            catch {
                return false;
            }
        }

        private static bool TryEnableWindowsVt() {
            try {
                IntPtr handle = GetStdHandle(StdOutputHandle);
                if (handle == IntPtr.Zero || handle == new IntPtr(-1)) {
                    return false;
                }

                if (!GetConsoleMode(handle, out uint mode)) {
                    return false;
                }

                uint requested = mode | EnableVirtualTerminalProcessing;
                if (!SetConsoleMode(handle, requested)) {
                    return false;
                }

                return true;
            }
            catch {
                return false;
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);
    }
}
