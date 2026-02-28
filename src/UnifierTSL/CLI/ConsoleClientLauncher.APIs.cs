namespace UnifierTSL.CLI
{
    public partial class ConsoleClientLauncher
    {
        #region Write overloads
        public override void Write(char value) => Write(value.ToString());
        public override void Write(bool value) => Write(value.ToString());
        public override void Write(float value) => Write(value.ToString());
        public override void Write(double value) => Write(value.ToString());
        public override void Write(decimal value) => Write(value.ToString());
        public override void Write(int value) => Write(value.ToString());
        public override void Write(uint value) => Write(value.ToString());
        public override void Write(long value) => Write(value.ToString());
        public override void Write(ulong value) => Write(value.ToString());
        public override void Write(object? value) => Write(value?.ToString());
        public override void Write(char[] buffer, int index, int count) => Write(new string(buffer, index, count));
        public override void Write(char[]? buffer) => Write(new string(buffer));
        public override void Write(string format, object? arg0) => Write(string.Format(format, arg0));
        public override void Write(string format, object? arg0, object? arg1) => Write(string.Format(format, arg0, arg1));
        public override void Write(string format, object? arg0, object? arg1, object? arg2) => Write(string.Format(format, arg0, arg1, arg2));
        public override void Write(string format, params object?[]? arg) => Write((arg is not null && arg.Length != 0) ? string.Format(format, arg) : format);
        public override void WriteLine() => WriteLine("");
        public override void WriteLine(bool value) => WriteLine(value.ToString());
        public override void WriteLine(char value) => WriteLine(value.ToString());
        public override void WriteLine(decimal value) => WriteLine(value.ToString());
        public override void WriteLine(double value) => WriteLine(value.ToString());
        public override void WriteLine(float value) => WriteLine(value.ToString());
        public override void WriteLine(int value) => WriteLine(value.ToString());
        public override void WriteLine(uint value) => WriteLine(value.ToString());
        public override void WriteLine(long value) => WriteLine(value.ToString());
        public override void WriteLine(ulong value) => WriteLine(value.ToString());
        public override void WriteLine(object? value) => WriteLine(value?.ToString() ?? "");
        public override void WriteLine(string format, object? arg0) => WriteLine(string.Format(format, arg0));
        public override void WriteLine(string format, object? arg0, object? arg1) => WriteLine(string.Format(format, arg0, arg1));
        public override void WriteLine(string format, object? arg0, object? arg1, object? arg2) => WriteLine(string.Format(format, arg0, arg1, arg2));
        public override void WriteLine(string format, params object?[]? arg) => WriteLine((arg is not null && arg.Length != 0) ? string.Format(format, arg) : format);
        public override void WriteLine(char[] buffer, int index, int count) => WriteLine(new string(buffer, index, count));
        public override void WriteLine(char[]? buffer) => WriteLine(new string(buffer));
        #endregion

        #region Others
        public sealed override void ResetColor() {
            ForegroundColor = ConsoleColor.Gray;
            BackgroundColor = ConsoleColor.Black;
        }
        #endregion
    }
}
