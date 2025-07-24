using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UnifierTSL.Events.Core
{
    public enum HandlerPriority : byte
    {
        Highest = 1,
        High = 2,
        Normal = 3,
        Low = 4,
        Lowest = 5,
    }
}
