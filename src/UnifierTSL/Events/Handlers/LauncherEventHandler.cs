using UnifierTSL.Events.Core;

namespace UnifierTSL.Events.Handlers
{
    public class LauncherEventHandler
    {
        public ReadonlyEventNoCancelProvider<InitializedEvent> InitializedEvent = new();
    }
    public struct InitializedEvent : IEventContent { }
}
