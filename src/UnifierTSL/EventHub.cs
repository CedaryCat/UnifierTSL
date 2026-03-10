using UnifierTSL.Events.Handlers;

namespace UnifierTSL
{
    public class EventHub
    {
        public readonly ConsoleLifecycleEventHandler Launcher = new();
        public readonly ChatHandler Chat = new();
        public readonly CoordinatorEventBridge Coordinator = new();
        public readonly GameEventBridge Game = new();
        public readonly NetplayEventBridge Netplay = new();
        public readonly ServerConsoleEventBridge Server = new();
    }
}
