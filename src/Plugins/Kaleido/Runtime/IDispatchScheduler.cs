using UnifierTSL.Servers;

namespace Kaleido.Runtime
{
    internal interface IDispatchScheduler
    {
        ServerDispatchDomain Domain { get; }
        IDisposable Register(string name, Action dispatch, Action stopped);
    }
}
