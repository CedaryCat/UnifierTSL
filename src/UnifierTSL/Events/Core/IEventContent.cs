using UnifiedServerProcess;

namespace UnifierTSL.Events.Core
{
    public interface IEventContent
    {
    }
    public interface IServerEventContent<TServer> : IEventContent where TServer : RootContext {
        TServer Server { get; }
    }
    public interface IPlayerEventContent : IEventContent
    {
        int Who { get; }
    }
}
