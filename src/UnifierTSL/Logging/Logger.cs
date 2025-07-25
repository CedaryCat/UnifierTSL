using System.Diagnostics.CodeAnalysis;
using UnifierTSL.Logging.LogFilters;

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
    }
}
