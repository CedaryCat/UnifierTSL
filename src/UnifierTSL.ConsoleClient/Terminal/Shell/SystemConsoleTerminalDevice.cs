using System.Text;

namespace UnifierTSL.Terminal.Shell {
    internal sealed class SystemConsoleTerminalDevice(IConsoleInterceptionBridge? bridge = null) : ITerminalDevice {
        private readonly TerminalCapabilities capabilities = TerminalCapabilities.Detect();

        public TerminalCapabilities Capabilities => capabilities;

        public TerminalViewport Viewport {
            get {
                bool hasWindowWidth = TryGetWindowWidth(out int windowWidth);
                int writableWidth = hasWindowWidth
                    ? Math.Max(10, windowWidth - 1)
                    : 119;

                bool hasBufferWidth = TryGetBufferWidth(out int bufferWidth);
                int wrapWidth = hasBufferWidth
                    ? Math.Max(1, bufferWidth)
                    : hasWindowWidth
                        ? Math.Max(1, windowWidth)
                        : writableWidth + 1;
                int normalizedBufferWidth = hasBufferWidth
                    ? Math.Max(1, bufferWidth)
                    : wrapWidth;
                int normalizedBufferHeight = TryGetBufferHeight(out int bufferHeight)
                    ? Math.Max(1, bufferHeight)
                    : 1;
                return new(writableWidth, wrapWidth, normalizedBufferWidth, normalizedBufferHeight);
            }
        }

        public TerminalCursor Cursor => new(Console.CursorLeft, Console.CursorTop);

        public ConsoleColor ForegroundColor {
            get => Console.ForegroundColor;
            set => Console.ForegroundColor = value;
        }

        public ConsoleColor BackgroundColor {
            get => Console.BackgroundColor;
            set => Console.BackgroundColor = value;
        }

        public void Write(string value) {
            if (bridge is null) {
                Console.Out.Write(value);
                return;
            }

            bridge.OriginalOut.Write(value);
        }

        public void WriteLine(string value) {
            if (bridge is null) {
                Console.Out.WriteLine(value);
                return;
            }

            bridge.OriginalOut.WriteLine(value);
        }

        public void Clear() {
            Console.Clear();
        }

        public void SetInputEncoding(Encoding encoding) {
            Console.InputEncoding = encoding;
        }

        public void SetOutputEncoding(Encoding encoding) {
            Console.OutputEncoding = encoding;
        }

        public void SetWindowWidth(int width) {
            if (!OperatingSystem.IsWindows()) {
                return;
            }

            Console.WindowWidth = width;
        }

        public void SetWindowHeight(int height) {
            if (!OperatingSystem.IsWindows()) {
                return;
            }

            Console.WindowHeight = height;
        }

        public void SetWindowLeft(int left) {
            if (!OperatingSystem.IsWindows()) {
                return;
            }

            Console.WindowLeft = left;
        }

        public void SetWindowTop(int top) {
            if (!OperatingSystem.IsWindows()) {
                return;
            }

            Console.WindowTop = top;
        }

        public void SetTitle(string title) {
            Console.Title = title;
        }

        public void SetCursorPosition(int left, int top) {
            Console.SetCursorPosition(left, top);
        }

        public void SetCursorVisible(bool visible) {
            Console.CursorVisible = visible;
        }

        public bool IsKeyAvailable() {
            if (bridge is null) {
                return Console.KeyAvailable;
            }

            using var _ = bridge.BeginRawAccess();
            return Console.KeyAvailable;
        }

        public ConsoleKeyInfo ReadKey(bool intercept) {
            if (bridge is null) {
                return Console.ReadKey(intercept);
            }

            using var _ = bridge.BeginRawAccess();
            return Console.ReadKey(intercept);
        }

        private static bool TryGetWindowWidth(out int width) {
            try {
                width = Console.WindowWidth;
                return true;
            }
            catch {
                width = 0;
                return false;
            }
        }

        private static bool TryGetBufferWidth(out int width) {
            try {
                width = Console.BufferWidth;
                return true;
            }
            catch {
                width = 0;
                return false;
            }
        }

        private static bool TryGetBufferHeight(out int height) {
            try {
                height = Console.BufferHeight;
                return true;
            }
            catch {
                height = 0;
                return false;
            }
        }
    }
}
