using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UnifierTSL.Logging.Formatters.ConsoleLog
{
    public readonly struct ColoredSegment(ReadOnlyMemory<char> text, ConsoleColor fgColor = ConsoleColor.White, ConsoleColor bgColor = ConsoleColor.Black) {
        public readonly ConsoleColor ForegroundColor = fgColor;
        public readonly ConsoleColor BackgroundColor = bgColor;
        public readonly ReadOnlyMemory<char> Text = text;
    }
}
