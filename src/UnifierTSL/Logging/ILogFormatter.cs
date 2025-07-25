using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UnifierTSL.Logging
{
    public interface IInspectableFormatter
    {
        string FormatName { get; }
        string Description { get; }
        dynamic Sample(in LogEntry entry);
    }
    public interface ILogFormatter<TOutPut> : IInspectableFormatter where TOutPut : notnull
    {
        public TOutPut Format(in LogEntry entry);
        dynamic IInspectableFormatter.Sample(in LogEntry entry) => Format(entry);
    }
}
