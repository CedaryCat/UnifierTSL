using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UnifierTSL.Events.Core
{
    public interface IPriorityHandler
    {
        HandlerPriority Priority { get; }
    }
}
