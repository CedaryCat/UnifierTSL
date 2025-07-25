using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnifierTSL.Logging.LogFilters;

namespace UnifierTSL.Logging
{
    public interface ILogFilter
    {
        public string Name => GetType().Name;
        bool ShouldLog(in LogEntry entry);
        public static ILogFilter operator &(ILogFilter left, ILogFilter right) {
            if (left is AndLogFilter leftAnd) {
                return leftAnd & right;
            }
            if (right is AndLogFilter rightAnd) {
                return left & rightAnd;
            }
            return new AndLogFilter(left, right);
        }

        public static ILogFilter operator |(ILogFilter left, ILogFilter right) {
            if (left is OrLogFilter leftOr) {
                return leftOr | right;
            }
            if (right is OrLogFilter rightOr) {
                return left | rightOr;
            }
            return new OrLogFilter(left, right);
        }
    }
}
