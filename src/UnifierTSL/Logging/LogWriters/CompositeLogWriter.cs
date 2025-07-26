using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using UnifierTSL.Logging.Formatters;

namespace UnifierTSL.Logging.LogWriters
{
    public class CompositeLogWriter : ILogWriter
    {
        public CompositeLogWriter(params IEnumerable<ILogWriter> writers) {
            Writers = [.. writers];
        }

        private CompositeLogWriter(ImmutableArray<ILogWriter> writers) {
            Writers = writers;
        }
        readonly ImmutableArray<ILogWriter> Writers;
        public void GetAvailableFormatters(out FormatterSelectionContext availableFormatters) {
            availableFormatters = FormatterSelectionContext.Empty;
        }

        public void Write(scoped in LogEntry log) {
            var writers = Writers.AsSpan();
            var len = writers.Length;
            if (len > 0) {
                ref var w0 = ref MemoryMarshal.GetReference(writers);
                for (int i = 0; i < len; i++) {
                    Unsafe.Add(ref w0, i).Write(in log);
                }
            }
        }

        public static CompositeLogWriter operator +(CompositeLogWriter left, CompositeLogWriter right) {
            return new CompositeLogWriter(left.Writers.AddRange(right.Writers));
        }

        public static CompositeLogWriter operator +(ILogWriter left, CompositeLogWriter right) {
            if (left is CompositeLogWriter leftAnd) {
                return leftAnd + right;
            }
            return new CompositeLogWriter(right.Writers.Insert(0, left));
        }

        public static CompositeLogWriter operator +(CompositeLogWriter left, ILogWriter right) {
            if (right is CompositeLogWriter rightAnd) {
                return left + rightAnd;
            }
            return new CompositeLogWriter(left.Writers.Add(right));
        }

        public static ILogWriter? operator -(CompositeLogWriter left, ILogWriter right) {
            var newWriters = left.Writers.Remove(right);
            if (newWriters.Length == 0) { 
                return null;
            }
            if (newWriters.Length == 1) {
                return newWriters[0];
            }

            return new CompositeLogWriter(newWriters);
        }

        public static ILogWriter? operator -(ILogWriter left, CompositeLogWriter right) {
            if (left is CompositeLogWriter leftAnd)
                return leftAnd - right;

            if (right.Writers.Contains(left))
                return null;

            return left;
        }

        public static ILogWriter? operator -(CompositeLogWriter left, CompositeLogWriter right) {
            var newWriters = left.Writers.ToHashSet();
            foreach (var writer in right.Writers) {
                newWriters.Remove(writer);
            }
            if (newWriters.Count == 0) {
                return null;
            }
            if (newWriters.Count == 1) {
                return newWriters.First();
            }
            return new CompositeLogWriter(newWriters);
        }
    }
}
