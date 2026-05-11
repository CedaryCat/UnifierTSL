using UnifierTSL.Contracts.Sessions;

namespace UnifierTSL.Terminal.Runtime
{
    internal sealed class TerminalSurfaceInteraction
    {
        public InteractionScope Scope { get; init; } = new();
        public TerminalSurfaceInteractionMode Mode { get; init; } = TerminalSurfaceInteractionMode.Editor;
        public EditorPaneRuntimeState EditorPane { get; set; } = new();
    }
}
