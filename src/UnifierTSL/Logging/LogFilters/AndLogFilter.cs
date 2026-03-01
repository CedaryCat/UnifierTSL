using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace UnifierTSL.Logging.LogFilters
{
    public class AndLogFilter(params IEnumerable<ILogFilter> filters) : ILogFilter
    {
        private ImmutableArray<ILogFilter> Filters = [.. filters.Where(x => x is not EmptyLogFilter)];

        public bool ShouldLog(in LogEntry entry) {
            ReadOnlySpan<ILogFilter> handlers = Filters.AsSpan();
            int len = handlers.Length;
            if (len == 0) {
                return true;
            }
            ref ILogFilter r0 = ref MemoryMarshal.GetReference(handlers);
            for (int i = 0; i < len; i++) {
                if (!Unsafe.Add(ref r0, i).ShouldLog(entry)) {
                    return false;
                }
            }
            return true;
        }
        public void AddFilter(ILogFilter filter) {
            Filters = Filters.Add(filter);
        }
        public static AndLogFilter operator &(AndLogFilter left, AndLogFilter right) {
            return new AndLogFilter(left.Filters.AddRange(right.Filters));
        }
        public static AndLogFilter operator &(ILogFilter left, AndLogFilter right) {
            if (left is AndLogFilter leftAnd) {
                return leftAnd & right;
            }
            return new AndLogFilter(right.Filters.Insert(0, left));
        }
        public static AndLogFilter operator &(AndLogFilter left, ILogFilter right) {
            if (right is AndLogFilter rightAnd) {
                return left & rightAnd;
            }
            return new AndLogFilter(left.Filters.Add(right));
        }
        public override string ToString() {
            return $"({string.Join(" & ", Filters)})";
        }
    }
}
