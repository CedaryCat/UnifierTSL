using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using UnifierTSL.Surface.Hosting;
using UnifierTSL.Terminal;
using UnifierTSL.Logging.Formatters.ConsoleLog;

namespace UnifierTSL.Logging.LogWriters
{
    public class ConsoleLogWriter() : LogWriter<ColoredSegment>(new DefConsoleFormatter())
    {
        public static readonly ConsoleLogWriter Instance = new();

        internal static string BuildAnsiLine(Span<ColoredSegment> input) {
            StringBuilder builder = new();
            ref ColoredSegment element0 = ref MemoryMarshal.GetReference(input);
            for (int i = 0; i < input.Length; i++) {
                ColoredSegment element = Unsafe.Add(ref element0, i);
                builder.Append(AnsiColorCodec.GetSgr(element.ForegroundColor, element.BackgroundColor));
                builder.Append(AnsiSanitizer.SanitizeEscapes(element.Text.ToString()));
            }
            builder.Append(AnsiColorCodec.Reset);
            return builder.ToString();
        }

        public sealed override void Write(scoped in LogEntry raw, Span<ColoredSegment> input) {
            int count = input.Length;
            if (count == 0) return;
            string ansiText = BuildAnsiLine(input);
            Console.WriteAnsi(ansiText);
        }
    }
}
