using UnifierTSL.Events.Core;
using UnifierTSL.Surface.Prompting;
using UnifierTSL.Surface.Hosting;
using UnifierTSL.Contracts.Projection;
using UnifierTSL.Servers;
using UnifierTSL.Surface.Prompting.Model;

namespace UnifierTSL.Events.Handlers
{
    public struct CreateLauncherSurfaceHostEvent : IEventContent
    {
        public ILauncherSurfaceHost? Host;
    }

    public class LauncherSurfaceEventHandler
    {
        public ValueEventNoCancelProvider<CreateLauncherSurfaceHostEvent> CreateLauncherSurfaceHost = new();
        public ValueEventNoCancelProvider<BuildSurfacePromptSummaryEvent> BuildSurfacePromptSummary = new();
        public ValueEventNoCancelProvider<BuildSurfaceStatusDocumentEvent> BuildSurfaceStatusDocument = new();
        public ReadonlyEventNoCancelProvider<InitializedEvent> InitializedEvent = new();

        public ILauncherSurfaceHost? InvokeCreateLauncherSurfaceHost() {
            CreateLauncherSurfaceHostEvent args = new();
            CreateLauncherSurfaceHost.Invoke(ref args);
            return args.Host;
        }
    }

    public struct BuildSurfacePromptSummaryEvent : IEventContent
    {
        public ServerContext? Server;
        public PromptInputPurpose Purpose;
        public PromptInputState State;
        public PromptSurfaceScenario Scenario;
        public string InputSummary;

        public BuildSurfacePromptSummaryEvent(
            ServerContext? server,
            PromptInputPurpose purpose,
            PromptInputState state,
            PromptSurfaceScenario scenario,
            string inputSummary) {
            Server = server;
            Purpose = purpose;
            State = state;
            Scenario = scenario;
            InputSummary = inputSummary;
        }
    }

    public struct BuildSurfaceStatusDocumentEvent : IEventContent
    {
        public ServerContext? Server;
        public ProjectionDocument? Document;

        public BuildSurfaceStatusDocumentEvent(
            ServerContext? server,
            ProjectionDocument? document) {
            Server = server;
            Document = document;
        }
    }

    public struct InitializedEvent : IEventContent { }
}
