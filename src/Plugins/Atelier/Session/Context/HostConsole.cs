namespace Atelier.Session.Context
{
    public sealed class HostConsole
    {
        private readonly Lock sync = new();
        private readonly ConsoleColor defaultForegroundColor = Console.ForegroundColor;
        private readonly ConsoleColor defaultBackgroundColor = Console.BackgroundColor;

        private Binding? binding;
        private ConsoleColor foregroundColor;
        private ConsoleColor backgroundColor;

        public HostConsole() {
            foregroundColor = defaultForegroundColor;
            backgroundColor = defaultBackgroundColor;
        }

        public ConsoleColor ForegroundColor {
            get {
                lock (sync) {
                    return foregroundColor;
                }
            }
            set {
                lock (sync) {
                    foregroundColor = value;
                }
            }
        }

        public ConsoleColor BackgroundColor {
            get {
                lock (sync) {
                    return backgroundColor;
                }
            }
            set {
                lock (sync) {
                    backgroundColor = value;
                }
            }
        }

        public void Bind(
            Action<string, ConsoleColor, ConsoleColor> write,
            Action<string, ConsoleColor, ConsoleColor> writeLine,
            Action clear,
            Func<string?> readLine,
            Func<int> read,
            Func<bool, ConsoleKeyInfo> readKey) {
            ArgumentNullException.ThrowIfNull(write);
            ArgumentNullException.ThrowIfNull(writeLine);
            ArgumentNullException.ThrowIfNull(clear);
            ArgumentNullException.ThrowIfNull(readLine);
            ArgumentNullException.ThrowIfNull(read);
            ArgumentNullException.ThrowIfNull(readKey);

            lock (sync) {
                binding = new Binding(write, writeLine, clear, readLine, read, readKey);
            }
        }

        public void Unbind() {
            lock (sync) {
                binding = null;
            }
        }

        public void Write(string? value) => write(value);
        public void Write(char value) => Write(value.ToString());
        public void Write(bool value) => Write(value.ToString());
        public void Write(float value) => Write(value.ToString());
        public void Write(double value) => Write(value.ToString());
        public void Write(decimal value) => Write(value.ToString());
        public void Write(int value) => Write(value.ToString());
        public void Write(uint value) => Write(value.ToString());
        public void Write(long value) => Write(value.ToString());
        public void Write(ulong value) => Write(value.ToString());
        public void Write(object? value) => Write(value?.ToString());
        public void Write(char[]? buffer) => Write(buffer is null ? null : new string(buffer));
        public void Write(char[] buffer, int index, int count) => Write(new string(buffer, index, count));
        public void Write(string format, object? arg0) => Write(string.Format(format, arg0));
        public void Write(string format, object? arg0, object? arg1) => Write(string.Format(format, arg0, arg1));
        public void Write(string format, object? arg0, object? arg1, object? arg2) => Write(string.Format(format, arg0, arg1, arg2));
        public void Write(string format, params object?[]? args) => Write(args is { Length: > 0 } ? string.Format(format, args) : format);

        public void WriteLine() => writeLine(string.Empty);
        public void WriteLine(string? value) => writeLine(value);
        public void WriteLine(char value) => WriteLine(value.ToString());
        public void WriteLine(bool value) => WriteLine(value.ToString());
        public void WriteLine(float value) => WriteLine(value.ToString());
        public void WriteLine(double value) => WriteLine(value.ToString());
        public void WriteLine(decimal value) => WriteLine(value.ToString());
        public void WriteLine(int value) => WriteLine(value.ToString());
        public void WriteLine(uint value) => WriteLine(value.ToString());
        public void WriteLine(long value) => WriteLine(value.ToString());
        public void WriteLine(ulong value) => WriteLine(value.ToString());
        public void WriteLine(object? value) => WriteLine(value?.ToString());
        public void WriteLine(char[]? buffer) => WriteLine(buffer is null ? null : new string(buffer));
        public void WriteLine(char[] buffer, int index, int count) => WriteLine(new string(buffer, index, count));
        public void WriteLine(string format, object? arg0) => WriteLine(string.Format(format, arg0));
        public void WriteLine(string format, object? arg0, object? arg1) => WriteLine(string.Format(format, arg0, arg1));
        public void WriteLine(string format, object? arg0, object? arg1, object? arg2) => WriteLine(string.Format(format, arg0, arg1, arg2));
        public void WriteLine(string format, params object?[]? args) => WriteLine(args is { Length: > 0 } ? string.Format(format, args) : format);

        public int Read() => ResolveBinding().Read();

        public string? ReadLine() => ResolveBinding().ReadLine();

        public ConsoleKeyInfo ReadKey() => ReadKey(intercept: false);

        public ConsoleKeyInfo ReadKey(bool intercept) => ResolveBinding().ReadKey(intercept);

        public void Clear() => ResolveBinding().Clear();

        public void ResetColor() {
            lock (sync) {
                foregroundColor = defaultForegroundColor;
                backgroundColor = defaultBackgroundColor;
            }
        }

        private void write(string? value) {
            if (string.IsNullOrEmpty(value)) {
                return;
            }

            var binding = ResolveBinding();
            var colors = SnapshotColors();
            binding.Write(value, colors.Foreground, colors.Background);
        }

        private void writeLine(string? value) {
            var binding = ResolveBinding();
            var colors = SnapshotColors();
            if (string.IsNullOrEmpty(value)) {
                binding.WriteLine(string.Empty, colors.Foreground, colors.Background);
                return;
            }

            binding.WriteLine(value, colors.Foreground, colors.Background);
        }

        private (ConsoleColor Foreground, ConsoleColor Background) SnapshotColors() {
            lock (sync) {
                return (foregroundColor, backgroundColor);
            }
        }

        private Binding ResolveBinding() {
            lock (sync) {
                return binding ?? throw new InvalidOperationException(GetString("Atelier console is not attached to an active REPL window."));
            }
        }

        private sealed record Binding(
            Action<string, ConsoleColor, ConsoleColor> Write,
            Action<string, ConsoleColor, ConsoleColor> WriteLine,
            Action Clear,
            Func<string?> ReadLine,
            Func<int> Read,
            Func<bool, ConsoleKeyInfo> ReadKey);
    }
}
