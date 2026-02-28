using UnifierTSL.Logging.LogFilters;

namespace UnifierTSL.Logging
{
    public interface ILogFilter
    {
        string Name => GetType().Name;
        bool ShouldLog(in LogEntry entry);
        static ILogFilter operator &(ILogFilter left, ILogFilter right) {
            if (left is AndLogFilter leftAnd) {
                return leftAnd & right;
            }
            if (right is AndLogFilter rightAnd) {
                return left & rightAnd;
            }
            return new AndLogFilter(left, right);
        }

        static ILogFilter operator |(ILogFilter left, ILogFilter right) {
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
