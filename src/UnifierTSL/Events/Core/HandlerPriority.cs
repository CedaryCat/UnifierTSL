using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UnifierTSL.Events.Core
{
    public enum HandlerPriority : byte
    {
        Highest = 0,
        VeryHigh = 10,
        Higher = 20,
        High = 30,

        AboveNormal = 40,
        Normal = 50,
        BelowNormal = 60,

        Low = 70,
        Lower = 80,
        VeryLow = 90,
        Lowest = 100
    }
}
