using UnifierTSL.Contracts.Projection;
using UnifierTSL.Contracts.Terminal.Overlay;

namespace UnifierTSL.Terminal.Shell
{
    internal readonly record struct TerminalOverlayLayoutPlan(
        OverlayControlDefinition? InlineAssistRoot,
        OverlayControlDefinition? PopupAssistRoot,
        OverlayControlDefinition? CompletionControl,
        OverlayControlDefinition? InterpretationAssistControl,
        OverlayControlDefinition? InterpretationStatusControl,
        OverlayControlDefinition? InputIndicatorControl,
        OverlayControlDefinition? GhostControl,
        OverlayControlDefinition? StatusHeaderControl,
        OverlayControlDefinition? StatusSummaryControl,
        OverlayControlDefinition? StatusBodyControl,
        bool SupportsPrevInterpretation,
        bool SupportsNextInterpretation,
        bool SupportsAcceptPreview,
        bool SupportsStatusScrollUp,
        bool SupportsStatusScrollDown)
    {
        public static readonly TerminalOverlayLayoutPlan Empty = Create(TerminalOverlay.Default);

        public bool UsesPopupCompletion => CompletionControl is { AnchorTarget: OverlayAnchorTarget.Caret };
        public bool UsesPopupAssist => UsesPopupCompletion
            || InterpretationAssistControl is { AnchorTarget: OverlayAnchorTarget.Caret };

        public static TerminalOverlayLayoutPlan Create(TerminalOverlay? overlay) {
            overlay ??= TerminalOverlay.Default;
            var completionControl = MatchControl(
                overlay.FindControlBySemanticSlot(EditorProjectionSemanticKeys.AssistPrimaryList),
                OverlayInteractionLayer.PrimaryAssist,
                OverlayControlKind.List);
            var interpretationControl = MatchInterpretationControl(FirstDefined(
                overlay.FindControlBySemanticSlot(EditorProjectionSemanticKeys.AssistSecondaryList),
                overlay.FindControlBySemanticSlot(EditorProjectionSemanticKeys.AssistSecondaryDetail)));
            var inputIndicatorControl = MatchControl(
                overlay.FindControlBySemanticSlot(EditorProjectionSemanticKeys.InputIndicator),
                OverlayInteractionLayer.Adornment,
                OverlayControlKind.Text,
                OverlayAnchorTarget.EditorLeft);
            var ghostControl = MatchControl(
                overlay.FindControlBySemanticSlot(EditorProjectionSemanticKeys.InputGhost),
                OverlayInteractionLayer.Adornment,
                OverlayControlKind.Text);
            var statusHeaderControl = MatchControl(
                overlay.FindControlBySemanticSlot(EditorProjectionSemanticKeys.StatusHeader),
                OverlayInteractionLayer.Status,
                OverlayControlKind.StatusBar,
                OverlayAnchorTarget.EditorTopLeft);
            var statusSummaryControl = MatchControl(
                overlay.FindControlBySemanticSlot(EditorProjectionSemanticKeys.StatusSummary),
                OverlayInteractionLayer.Status,
                OverlayControlKind.StatusBar,
                OverlayAnchorTarget.EditorTopLeft);
            var statusBodyControl = MatchControl(
                overlay.FindControlBySemanticSlot(EditorProjectionSemanticKeys.StatusDetail),
                OverlayInteractionLayer.Status,
                OverlayControlKind.Text,
                OverlayAnchorTarget.EditorTopLeft);
            return new TerminalOverlayLayoutPlan(
                InlineAssistRoot: FindAssistRoot(overlay, OverlayAnchorTarget.EditorBottomLeft),
                PopupAssistRoot: FindAssistRoot(overlay, OverlayAnchorTarget.Caret),
                CompletionControl: completionControl,
                InterpretationAssistControl: interpretationControl,
                InterpretationStatusControl: MatchControl(
                    interpretationControl,
                    OverlayInteractionLayer.Status,
                    OverlayControlKind.Text,
                    OverlayAnchorTarget.EditorTopLeft),
                InputIndicatorControl: inputIndicatorControl,
                GhostControl: ghostControl,
                StatusHeaderControl: statusHeaderControl,
                StatusSummaryControl: statusSummaryControl,
                StatusBodyControl: statusBodyControl,
                SupportsPrevInterpretation: interpretationControl is not null,
                SupportsNextInterpretation: interpretationControl is not null,
                SupportsAcceptPreview: ghostControl is not null,
                SupportsStatusScrollUp: statusBodyControl is not null,
                SupportsStatusScrollDown: statusBodyControl is not null);
        }

        private static OverlayControlDefinition? FindAssistRoot(
            TerminalOverlay overlay,
            OverlayAnchorTarget anchorTarget) {
            return (overlay.Definition.Overlay.Roots ?? [])
                .FirstOrDefault(control => control is {
                    Kind: OverlayControlKind.Stack,
                    InteractionLayer: OverlayInteractionLayer.PrimaryAssist,
                    AnchorTarget: var target
                } && target == anchorTarget);
        }

        private static OverlayControlDefinition? MatchInterpretationControl(OverlayControlDefinition? control) {
            return control is { Kind: OverlayControlKind.Text, InteractionLayer: var layer }
                && (layer == OverlayInteractionLayer.SecondaryAssist || layer == OverlayInteractionLayer.Status)
                ? control
                : null;
        }

        private static OverlayControlDefinition? MatchControl(
            OverlayControlDefinition? control,
            OverlayInteractionLayer interactionLayer,
            OverlayControlKind controlKind,
            OverlayAnchorTarget? anchorTarget = null) {
            return control is { InteractionLayer: var layer, Kind: var kind, AnchorTarget: var target }
                && layer == interactionLayer
                && kind == controlKind
                && (!anchorTarget.HasValue || target == anchorTarget.Value)
                ? control
                : null;
        }

        private static OverlayControlDefinition? FirstDefined(params OverlayControlDefinition?[] controls) {
            return (controls ?? []).FirstOrDefault(static control => control is not null);
        }
    }
}
