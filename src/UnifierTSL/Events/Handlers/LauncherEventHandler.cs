using UnifierTSL.Events.Core;
using UnifierTSL.CLI;

namespace UnifierTSL.Events.Handlers
{
    public struct CreateLauncherConsoleFrontendEvent : IEventContent
    {
        public ILauncherConsoleFrontend? Frontend;
    }

    public class LauncherEventHandler
    {
        public ValueEventNoCancelProvider<CreateLauncherConsoleFrontendEvent> CreateLauncherConsoleFrontend = new();
        public ReadonlyEventNoCancelProvider<InitializedEvent> InitializedEvent = new();

        public ILauncherConsoleFrontend? InvokeCreateLauncherConsoleFrontend()
        {
            CreateLauncherConsoleFrontendEvent args = new();
            CreateLauncherConsoleFrontend.Invoke(ref args);
            return args.Frontend;
        }
    }
    public struct InitializedEvent : IEventContent { }
}
