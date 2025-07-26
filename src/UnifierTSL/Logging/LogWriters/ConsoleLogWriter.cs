using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using UnifierTSL.Logging.Formatters.ConsoleLog;

namespace UnifierTSL.Logging.LogWriters
{
    public class ConsoleLogWriter() : LogWriter<ColoredSegment>(new DefConsoleFormatter())
    {
        public readonly static ConsoleLogWriter Instance = new ConsoleLogWriter();
        public sealed override void Write(Span<ColoredSegment> input) {
            int count = input.Length;
            if (count == 0) return;
            ref var element0 = ref MemoryMarshal.GetReference(input);
            for (int i = 0; i < count; i++) {
                var element = Unsafe.Add(ref element0, i);
                Console.BackgroundColor = element.BackgroundColor;
                Console.ForegroundColor = element.ForegroundColor;
                Console.Write(element.Text);
                Console.ResetColor();
            }
        }
    }
}
