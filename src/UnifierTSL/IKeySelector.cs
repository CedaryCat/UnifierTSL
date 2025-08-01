using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UnifierTSL
{
    public interface IKeySelector<TKey> where TKey : notnull
    {
        TKey Key { get; }
    }
}
