using System.Diagnostics.CodeAnalysis;
using UnifierTSL.Logging.LogFilters;
using UnifierTSL.Logging.LogWriters;

namespace UnifierTSL.Logging
{
    public class Logger
    {
        private ILogFilter filter = new EmptyLogFilter();
        public ILogFilter? Filter {
            [return: NotNull]
            get => filter;
            set {
                if (value is null) {
                    filter = EmptyLogFilter.Instance;
                    return;
                }
                filter = value;
            }
        }
        private ILogWriter writer;
        public ILogWriter? Writer {
            [return: NotNull]
            get => writer;
            set {
                if (value is null) {
                    writer = new ConsoleLogWriter();
                    return;
                }
                writer = value;
            }
        }
    }
}
