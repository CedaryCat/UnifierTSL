using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UnifierTSL.Logging
{
    public interface IMetadataInjectionHost
    {
        void AddMetadataInjector(ILogMetadataInjector injector);
        void RemoveMetadataInjector(ILogMetadataInjector injector);
        IReadOnlyList<ILogMetadataInjector> MetadataInjectors { get; }
    }
}
