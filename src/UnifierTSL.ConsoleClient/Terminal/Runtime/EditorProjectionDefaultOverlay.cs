using UnifierTSL.Contracts.Display;
using UnifierTSL.Contracts.Projection;
using UnifierTSL.Contracts.Terminal;
using UnifierTSL.Contracts.Terminal.Overlay;

namespace UnifierTSL.Terminal.Runtime {
    internal static class EditorProjectionDefaultOverlay {
        private static class StyleKeys {
            public const string StatusBand = SurfaceStyleCatalog.StatusBand;
            public const string StatusTitle = SurfaceStyleCatalog.StatusTitle;
            public const string StatusSummary = SurfaceStyleCatalog.StatusSummary;
        }

        private const string InlineOverlayKey = "overlay.editor.inline";
        private const string InlineOverlayNoStatusKey = "overlay.editor.inline.no-status";
        private const string PopupAssistOverlayKey = "overlay.editor.popup-assist";
        private const string PopupAssistOverlayNoStatusKey = "overlay.editor.popup-assist.no-status";
        private const string GhostControlKey = "overlay.editor.ghost";
        private const string InputIndicatorControlKey = "overlay.editor.input.indicator";
        private const string AssistControlKey = "overlay.editor.assist";
        private const string StatusControlKey = "overlay.editor.status";
        private const string StatusHeaderControlKey = "overlay.editor.status.header";
        private const string StatusSummaryControlKey = "overlay.editor.status.summary";
        private const string StatusBodyControlKey = "overlay.editor.status.body";
        private const string CompletionControlKey = "overlay.editor.completion";
        private const string InterpretationControlKey = "overlay.editor.interpretation";
        private const int PopupAssistMinHorizontalViewportMargin = 4;
        private static readonly TerminalOverlay inlineOverlay = CreateInlineOverlay(includeStatus: true);
        private static readonly TerminalOverlay inlineOverlayNoStatus = CreateInlineOverlay(includeStatus: false);
        private static readonly TerminalOverlay popupAssistOverlay = CreatePopupAssistOverlay(includeStatus: true);
        private static readonly TerminalOverlay popupAssistOverlayNoStatus = CreatePopupAssistOverlay(includeStatus: false);

        public static TerminalOverlay Resolve(bool usesPopupAssist, bool includeStatus) {
            return (usesPopupAssist, includeStatus) switch {
                (true, true) => popupAssistOverlay,
                (true, false) => popupAssistOverlayNoStatus,
                (false, true) => inlineOverlay,
                _ => inlineOverlayNoStatus,
            };
        }

        private static TerminalOverlay CreateInlineOverlay(bool includeStatus) {
            var builder = new TerminalOverlayBuilder {
                OverlayKey = includeStatus ? InlineOverlayKey : InlineOverlayNoStatusKey,
            };
            List<OverlayControlDefinition> controls = [
                builder.Text(
                    InputIndicatorControlKey,
                    OverlayInteractionLayer.Adornment,
                    OverlayAnchorTarget.EditorLeft,
                    new TextTemplate {
                        MaxLines = 1,
                        PreserveWhitespace = true,
                    },
                    semanticSlot: EditorProjectionSemanticKeys.InputIndicator),
                builder.Text(
                    GhostControlKey,
                    OverlayInteractionLayer.Adornment,
                    OverlayAnchorTarget.BufferEnd,
                    semanticSlot: EditorProjectionSemanticKeys.InputGhost),
            ];
            if (includeStatus) {
                controls.Add(builder.Stack(
                    StatusControlKey,
                    OverlayInteractionLayer.Status,
                    OverlayAnchorTarget.EditorTopLeft,
                    new StackTemplate(),
                    children: [
                        CreateStatusHeader(builder),
                        CreateStatusSummary(builder),
                        CreateStatusBody(builder),
                        CreateInlineInterpretationControl(builder),
                    ]));
            }
            else {
                controls.Add(CreateInlineInterpretationControl(builder));
            }

            return builder.Build([.. controls]);
        }

        private static TerminalOverlay CreatePopupAssistOverlay(bool includeStatus) {
            var builder = new TerminalOverlayBuilder {
                OverlayKey = includeStatus ? PopupAssistOverlayKey : PopupAssistOverlayNoStatusKey,
            };
            List<OverlayControlDefinition> controls = [
                builder.Text(
                    InputIndicatorControlKey,
                    OverlayInteractionLayer.Adornment,
                    OverlayAnchorTarget.EditorLeft,
                    new TextTemplate {
                        MaxLines = 1,
                        PreserveWhitespace = true,
                    },
                    semanticSlot: EditorProjectionSemanticKeys.InputIndicator),
                builder.Text(
                    GhostControlKey,
                    OverlayInteractionLayer.Adornment,
                    OverlayAnchorTarget.BufferEnd,
                    semanticSlot: EditorProjectionSemanticKeys.InputGhost),
                builder.Stack(
                    AssistControlKey,
                    OverlayInteractionLayer.PrimaryAssist,
                    OverlayAnchorTarget.Caret,
                    new StackTemplate(),
                    overflowPolicy: OverlayOverflowPolicy.PreferThenFlip,
                    minHorizontalViewportMargin: PopupAssistMinHorizontalViewportMargin,
                    children: [
                        builder.Text(
                            InterpretationControlKey,
                            OverlayInteractionLayer.SecondaryAssist,
                            OverlayAnchorTarget.Caret,
                            new TextTemplate {
                                Wrap = true,
                                MaxLines = 6,
                                PreserveWhitespace = true,
                            },
                            semanticSlot: EditorProjectionSemanticKeys.AssistSecondaryList),
                        builder.List(
                            CompletionControlKey,
                            OverlayInteractionLayer.PrimaryAssist,
                            OverlayAnchorTarget.Caret,
                            new ListTemplate {
                                MaxVisibleItems = 9,
                                DetailPlacement = OverlayListDetailPlacement.RightPopout,
                            },
                            semanticSlot: EditorProjectionSemanticKeys.AssistPrimaryList),
                    ]),
            ];
            if (includeStatus) {
                controls.Insert(2, builder.Stack(
                    StatusControlKey,
                    OverlayInteractionLayer.Status,
                    OverlayAnchorTarget.EditorTopLeft,
                    new StackTemplate(),
                    children: [
                        CreateStatusHeader(builder),
                        CreateStatusSummary(builder),
                        CreateStatusBody(builder),
                    ]));
            }

            return builder.Build([.. controls]);
        }

        private static OverlayControlDefinition CreateInlineInterpretationControl(TerminalOverlayBuilder builder) {
            return builder.Text(
                InterpretationControlKey,
                OverlayInteractionLayer.Status,
                OverlayAnchorTarget.EditorTopLeft,
                new TextTemplate {
                    Wrap = true,
                    PreserveWhitespace = true,
                },
                semanticSlot: EditorProjectionSemanticKeys.AssistSecondaryList);
        }

        private static OverlayControlDefinition CreateStatusHeader(TerminalOverlayBuilder builder) {
            return builder.StatusBar(
                StatusHeaderControlKey,
                OverlayInteractionLayer.Status,
                OverlayAnchorTarget.EditorTopLeft,
                new StatusBarTemplate {
                    Rows = [
                        new StatusBarRowTemplate {
                            Id = "main",
                            StyleKey = StyleKeys.StatusBand,
                            Fields = [
                                new StatusBarFieldTemplate {
                                    FieldKey = SurfaceStatusFieldKeys.Title,
                                    StyleKey = StyleKeys.StatusTitle,
                                },
                                new StatusBarFieldTemplate {
                                    FieldKey = SurfaceStatusFieldKeys.Summary,
                                    StyleKey = StyleKeys.StatusSummary,
                                    PrefixText = " ",
                                },
                            ],
                        },
                    ],
                },
                semanticSlot: EditorProjectionSemanticKeys.StatusHeader);
        }

        private static OverlayControlDefinition CreateStatusSummary(TerminalOverlayBuilder builder) {
            return builder.StatusBar(
                StatusSummaryControlKey,
                OverlayInteractionLayer.Status,
                OverlayAnchorTarget.EditorTopLeft,
                new StatusBarTemplate {
                    Rows = [
                        new StatusBarRowTemplate {
                            Id = "summary",
                            Fields = [
                                new StatusBarFieldTemplate {
                                    FieldKey = SurfaceStatusFieldKeys.Summary,
                                    StyleKey = StyleKeys.StatusSummary,
                                },
                            ],
                        },
                    ],
                },
                semanticSlot: EditorProjectionSemanticKeys.StatusSummary);
        }

        private static OverlayControlDefinition CreateStatusBody(TerminalOverlayBuilder builder) {
            return builder.Text(
                StatusBodyControlKey,
                OverlayInteractionLayer.Status,
                OverlayAnchorTarget.EditorTopLeft,
                new TextTemplate {
                    Wrap = true,
                    PreserveWhitespace = true,
                },
                semanticSlot: EditorProjectionSemanticKeys.StatusDetail);
        }
    }
}
