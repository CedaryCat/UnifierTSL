using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UnifierTSL.Logging.LogFilters
{
    public class EmptyLogFilter : ILogFilter
    {
        public bool ShouldLog(in LogEntry entry) => true;
        public static readonly EmptyLogFilter Instance = new();
    }
}
