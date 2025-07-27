using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnifierTSL.Logging;

namespace UnifierTSL
{
    public partial class UnifierApi
    {
        public static RoleLogger CreateLogger(ILoggerHost host, Logger? overrideLogger = null) {
            return new RoleLogger(host, overrideLogger ?? LogCore);
        }
    }
}
