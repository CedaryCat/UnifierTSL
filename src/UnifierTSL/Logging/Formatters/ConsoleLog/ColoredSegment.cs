using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UnifierTSL.Logging.Formatters.ConsoleLog
{
    public readonly struct ColoredSegment(ReadOnlyMemory<char> Text, ConsoleColor FgColor = ConsoleColor.White, ConsoleColor BgColor = ConsoleColor.Black) {
        public readonly ConsoleColor ForegroundColor = FgColor;
        public readonly ConsoleColor BackgroundColor = BgColor;
        public readonly ReadOnlyMemory<char> Text;
    }
}
