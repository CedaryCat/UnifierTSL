using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnifierTSL.Logging.Formatters.ConsoleLog;

namespace UnifierTSL.Logging.LogWriters
{
    public class ConsoleLogWriter : LogWriter<ReadOnlyMemory<ColoredSegment>>
    {
        public ConsoleLogWriter() : base(new DefConsoleFormatter()) {
        }

        public override void Write(ReadOnlyMemory<ColoredSegment> input) {
            throw new NotImplementedException();
        }
    }
}
