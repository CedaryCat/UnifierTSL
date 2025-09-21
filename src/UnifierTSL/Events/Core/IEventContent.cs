using UnifierTSL.Servers;

namespace UnifierTSL.Events.Core
{
    public interface IEventContent
    {
    }
    public interface IServerEventContent : IEventContent
    {
        ServerContext Server { get; }
    }
    public interface IPlayerEventContent : IServerEventContent
    {
        int Who { get; }
    }
}
