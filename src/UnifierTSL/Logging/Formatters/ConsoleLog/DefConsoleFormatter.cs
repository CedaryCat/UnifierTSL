using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UnifierTSL.Logging.Formatters.ConsoleLog
{
    public class DefConsoleFormatter : ILogFormatter<ReadOnlyMemory<ColoredSegment>>
    {
        public string FormatName => "Default Console Formatter";

        public string Description => "A default console formatter implementation";

        public ReadOnlyMemory<ColoredSegment> Format(in LogEntry entry) {

        }
    }
}
