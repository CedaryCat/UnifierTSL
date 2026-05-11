namespace UnifierTSL.Servers
{
    public partial class ServerContext
    {
        public ServerDispatcher Dispatcher { get; }

        protected virtual ServerDispatcher CreateDispatcher() => new UpdateThreadServerDispatcher(this);
    }
}
