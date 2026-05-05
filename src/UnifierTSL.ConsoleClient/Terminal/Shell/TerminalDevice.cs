using System.Text;

namespace UnifierTSL.Terminal.Shell {
    internal interface ITerminalDevice {
        TerminalCapabilities Capabilities { get; }

        TerminalViewport Viewport { get; }

        TerminalCursor Cursor { get; }

        ConsoleColor ForegroundColor { get; set; }

        ConsoleColor BackgroundColor { get; set; }

        void Write(string value);

        void WriteLine(string value);

        void Clear();

        void SetInputEncoding(Encoding encoding);

        void SetOutputEncoding(Encoding encoding);

        void SetWindowWidth(int width);

        void SetWindowHeight(int height);

        void SetWindowLeft(int left);

        void SetWindowTop(int top);

        void SetTitle(string title);

        void SetCursorPosition(int left, int top);

        void SetCursorVisible(bool visible);

        bool IsKeyAvailable();

        ConsoleKeyInfo ReadKey(bool intercept);
    }

    internal readonly record struct TerminalViewport(int WritableWidth, int WrapWidth, int BufferWidth, int BufferHeight) {
        public int ClampRow(int row) {
            return Math.Clamp(row, 0, Math.Max(0, BufferHeight - 1));
        }

        public int ClampColumn(int column) {
            return Math.Clamp(column, 0, Math.Max(0, BufferWidth - 1));
        }
    }

    internal readonly record struct TerminalCursor(int Left, int Top) {
        public TerminalCursor Clamp(TerminalViewport viewport) {
            return new(viewport.ClampColumn(Left), viewport.ClampRow(Top));
        }
    }
}
