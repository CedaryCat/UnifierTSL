using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UnifierTSL.Events.Core
{
    public enum FilterEventOption : byte
    {
        Normal = 1,
        Handled = 2,
        All = Normal | Handled,
    }
}
