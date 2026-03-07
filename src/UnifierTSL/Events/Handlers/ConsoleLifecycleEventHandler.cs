using UnifierTSL.Events.Core;
using UnifierTSL.CLI;
using UnifierTSL.CLI.Prompting;
using UnifierTSL.CLI.Status;
using UnifierTSL.ConsoleClient.Shared.ConsolePrompting;
using UnifierTSL.Servers;

namespace UnifierTSL.Events.Handlers
{
    public struct CreateLauncherConsoleHostEvent : IEventContent
    {
        public ILauncherConsoleHost? Host;
    }

    public class ConsoleLifecycleEventHandler
    {
        public ValueEventNoCancelProvider<CreateLauncherConsoleHostEvent> CreateLauncherConsoleHost = new();
        public ValueEventNoCancelProvider<BuildConsolePromptSummaryEvent> BuildConsolePromptSummary = new();
        public ValueEventNoCancelProvider<BuildConsoleStatusFrameEvent> BuildConsoleStatusFrame = new();
        public ReadonlyEventNoCancelProvider<InitializedEvent> InitializedEvent = new();

        public ILauncherConsoleHost? InvokeCreateLauncherConsoleHost() {
            CreateLauncherConsoleHostEvent args = new();
            CreateLauncherConsoleHost.Invoke(ref args);
            return args.Host;
        }
    }

    public struct BuildConsolePromptSummaryEvent : IEventContent
    {
        public ServerContext? Server;
        public ConsoleInputPurpose Purpose;
        public ConsoleInputState State;
        public ConsolePromptScenario Scenario;
        public string InputSummary;

        public BuildConsolePromptSummaryEvent(
            ServerContext? server,
            ConsoleInputPurpose purpose,
            ConsoleInputState state,
            ConsolePromptScenario scenario,
            string inputSummary) {
            Server = server;
            Purpose = purpose;
            State = state;
            Scenario = scenario;
            InputSummary = inputSummary;
        }
    }

    public struct BuildConsoleStatusFrameEvent : IEventContent
    {
        public ServerContext? Server;
        public ConsoleStatusResolveContext Context;
        public ConsoleStatusFrame? Frame;

        public BuildConsoleStatusFrameEvent(
            ServerContext? server,
            ConsoleStatusResolveContext context,
            ConsoleStatusFrame? frame) {
            Server = server;
            Context = context;
            Frame = frame;
        }
    }

    public struct InitializedEvent : IEventContent { }
}
