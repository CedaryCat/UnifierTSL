using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UnifierTSL.Logging
{
    public interface ILogMetadataInjector
    {
        void InjectMetadata(scoped ref LogEntry entry);
    }
}
