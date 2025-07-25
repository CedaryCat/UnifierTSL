using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UnifierTSL.Logging.LogFilters
{
    public delegate bool Predicate(in LogEntry item);
    public class ExpressionLogFilter(Predicate predicate) : ILogFilter
    {
        private readonly Predicate _predicate = predicate;
        public bool ShouldLog(in LogEntry entry) => _predicate(entry);
        public override string ToString() => $"ExpFilter: {_predicate.Method.Name}";
    }
}
