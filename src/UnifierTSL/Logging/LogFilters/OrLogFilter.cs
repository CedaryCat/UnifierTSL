using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace UnifierTSL.Logging.LogFilters
{
    public class OrLogFilter(params IEnumerable<ILogFilter> filters) : ILogFilter
    {
        private ImmutableArray<ILogFilter> Filters = [.. filters.Where(x => x is not EmptyLogFilter)];

        public bool ShouldLog(in LogEntry entry) {
            var handlers = Filters.AsSpan();
            var len = handlers.Length;
            ref var r0 = ref MemoryMarshal.GetReference(handlers);
            for (int i = 0; i < len; i++) {
                if (Unsafe.Add(ref r0, i).ShouldLog(entry)) {
                    return true;
                }
            }
            return true;
        }
        public void AddFilter(ILogFilter filter) {
            Filters = Filters.Add(filter);
        }
        public static OrLogFilter operator |(OrLogFilter left, OrLogFilter right) {
            return new OrLogFilter(left.Filters.AddRange(right.Filters));
        }
        public static OrLogFilter operator |(ILogFilter left, OrLogFilter right) {
            if (left is OrLogFilter leftAnd) {
                return leftAnd | right;
            }
            return new OrLogFilter(right.Filters.Insert(0, left));
        }
        public static OrLogFilter operator |(OrLogFilter left, ILogFilter right) {
            if (right is OrLogFilter rightAnd) {
                return left | rightAnd;
            }
            return new OrLogFilter(left.Filters.Add(right));
        }
        public override string ToString() {
            return $"({string.Join(" | ", Filters)})";
        }
    }
}
