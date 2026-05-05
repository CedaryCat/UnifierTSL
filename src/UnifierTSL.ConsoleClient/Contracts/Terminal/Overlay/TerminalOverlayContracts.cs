namespace UnifierTSL.Contracts.Terminal.Overlay {
    /// <summary>
    /// Identifies the presentation primitive used by an overlay control.
    /// </summary>
    public enum OverlayControlKind : byte {
        /// <summary>
        /// A container that lays out child controls.
        /// </summary>
        Stack,

        /// <summary>
        /// A text presenter.
        /// </summary>
        Text,

        /// <summary>
        /// A list presenter, typically used for completion or selection UIs.
        /// </summary>
        List,

        /// <summary>
        /// A status-row presenter that formats field-based status content.
        /// </summary>
        StatusBar,
    }

    /// <summary>
    /// Describes the semantic interaction layer for an overlay control.
    /// </summary>
    public enum OverlayInteractionLayer : byte {
        /// <summary>
        /// Non-interactive content that passively augments the editor.
        /// </summary>
        Passive,

        /// <summary>
        /// The primary assist surface, usually used for completion and selection flows.
        /// </summary>
        PrimaryAssist,

        /// <summary>
        /// A secondary assist surface, usually used for contextual hints beside a primary assist.
        /// </summary>
        SecondaryAssist,

        /// <summary>
        /// Status-oriented content rendered as prompt context rather than assist UI.
        /// </summary>
        Status,

        /// <summary>
        /// Visual adornments anchored to the editor without being treated as status or assist content.
        /// </summary>
        Adornment,
    }

    /// <summary>
    /// Describes where an overlay control is anchored relative to the editor.
    /// </summary>
    public enum OverlayAnchorTarget : byte {
        /// <summary>
        /// Anchor the control to the current caret position.
        /// </summary>
        Caret,

        /// <summary>
        /// Anchor the control to the logical end of the current buffer.
        /// </summary>
        BufferEnd,

        /// <summary>
        /// Anchor the control to the top-left corner of the editor region.
        /// </summary>
        EditorTopLeft,

        /// <summary>
        /// Anchor the control to the bottom-left corner of the editor region.
        /// </summary>
        EditorBottomLeft,

        /// <summary>
        /// Anchor the control to the left edge of the editor input row.
        /// </summary>
        EditorLeft,
    }

    /// <summary>
    /// Controls how an overlay chooses its horizontal width.
    /// </summary>
    public enum OverlayWidthPolicy : byte {
        /// <summary>
        /// Size the control to its own content.
        /// </summary>
        Content,

        /// <summary>
        /// Expand the control to the full writable viewport width.
        /// </summary>
        FillViewportWidth,
    }

    /// <summary>
    /// Controls how an overlay responds when its preferred placement would overflow.
    /// </summary>
    public enum OverlayOverflowPolicy : byte {
        /// <summary>
        /// Try the preferred side first, then flip to the opposite side when needed.
        /// </summary>
        PreferThenFlip,

        /// <summary>
        /// Try the preferred side first, then clamp the content within the viewport bounds.
        /// </summary>
        PreferThenClamp,
    }

    /// <summary>
    /// Controls where list-detail content is shown relative to the main list body.
    /// </summary>
    public enum OverlayListDetailPlacement : byte {
        /// <summary>
        /// Do not render a separate detail panel.
        /// </summary>
        None,

        /// <summary>
        /// Render the detail panel as a right-side popout.
        /// </summary>
        RightPopout,

        /// <summary>
        /// Render the detail panel below the main list.
        /// </summary>
        BottomPanel,
    }

    /// <summary>
    /// Controls whether a status-bar row participates in full and/or compact rendering.
    /// </summary>
    public enum StatusRowVisibility : byte {
        /// <summary>
        /// Always render the row when it has content.
        /// </summary>
        Always,

        /// <summary>
        /// Only render the row in the full status presentation.
        /// </summary>
        FullOnly,

        /// <summary>
        /// Only render the row in the compact status presentation.
        /// </summary>
        CompactOnly,
    }

    /// <summary>
    /// Sets the relative importance of a status field when compact layouts must drop content.
    /// </summary>
    public enum StatusFieldCompactPriority : byte {
        /// <summary>
        /// Hide the field in compact layouts.
        /// </summary>
        Hidden,

        /// <summary>
        /// Low-priority field that can be dropped early.
        /// </summary>
        Low,

        /// <summary>
        /// Normal-priority field.
        /// </summary>
        Normal,

        /// <summary>
        /// High-priority field that should be retained when possible.
        /// </summary>
        High,
    }

    /// <summary>
    /// Defines the overlay tree and presenter templates that make up a terminal overlay.
    /// </summary>
    public sealed class TerminalOverlayDefinition {
        /// <summary>
        /// Root overlay controls for the overlay.
        /// </summary>
        public OverlayRootDefinition Overlay { get; init; } = new();

        /// <summary>
        /// Presenter templates referenced by overlay controls in <see cref="Overlay"/>.
        /// </summary>
        public SurfaceTemplateDictionary Templates { get; init; } = new();
    }

    /// <summary>
    /// Holds the root overlay controls for an overlay.
    /// </summary>
    public sealed class OverlayRootDefinition {
        /// <summary>
        /// Top-level controls. Child controls are reached recursively from these roots.
        /// </summary>
        public OverlayControlDefinition[] Roots { get; init; } = [];
    }

    /// <summary>
    /// Describes a single overlay control in the overlay tree.
    /// </summary>
    public sealed class OverlayControlDefinition {
        /// <summary>
        /// Stable identifier used for action binding and control lookup.
        /// </summary>
        public string ControlKey { get; init; } = string.Empty;

        /// <summary>
        /// Open-ended editor-owned semantic slot resolved by local runtimes.
        /// </summary>
        public string SemanticSlot { get; init; } = string.Empty;

        /// <summary>
        /// Presenter kind used to render this control.
        /// </summary>
        public OverlayControlKind Kind { get; init; }

        /// <summary>
        /// Semantic interaction layer for the control.
        /// </summary>
        public OverlayInteractionLayer InteractionLayer { get; init; }

        /// <summary>
        /// Anchor location used when placing the control.
        /// </summary>
        public OverlayAnchorTarget AnchorTarget { get; init; }

        /// <summary>
        /// Horizontal sizing policy.
        /// </summary>
        public OverlayWidthPolicy WidthPolicy { get; init; } = OverlayWidthPolicy.Content;

        /// <summary>
        /// Overflow handling policy applied when the preferred placement does not fit.
        /// </summary>
        public OverlayOverflowPolicy OverflowPolicy { get; init; } = OverlayOverflowPolicy.PreferThenClamp;

        /// <summary>
        /// Preferred minimum horizontal distance, in terminal cells, between this control and the viewport edges.
        /// Renderers may reduce it when the viewport is too narrow to satisfy both margins.
        /// </summary>
        public int MinHorizontalViewportMargin { get; init; }

        /// <summary>
        /// Identifier of the presenter template that styles this control.
        /// </summary>
        public string PresenterTemplateId { get; init; } = string.Empty;

        /// <summary>
        /// Nested child controls for stack-like compositions.
        /// </summary>
        public OverlayControlDefinition[] Children { get; init; } = [];
    }

    /// <summary>
    /// Groups presenter templates addressable by <see cref="OverlayControlDefinition.PresenterTemplateId"/>.
    /// </summary>
    public sealed class SurfaceTemplateDictionary {
        /// <summary>
        /// Registered text templates.
        /// </summary>
        public TextTemplate[] Texts { get; init; } = [];

        /// <summary>
        /// Registered list templates.
        /// </summary>
        public ListTemplate[] Lists { get; init; } = [];

        /// <summary>
        /// Registered stack templates.
        /// </summary>
        public StackTemplate[] Stacks { get; init; } = [];

        /// <summary>
        /// Registered status-bar templates.
        /// </summary>
        public StatusBarTemplate[] StatusBars { get; init; } = [];
    }

    /// <summary>
    /// Template settings for a text presenter.
    /// </summary>
    public sealed class TextTemplate {
        /// <summary>
        /// Stable template identifier referenced by controls.
        /// </summary>
        public string Id { get; init; } = string.Empty;

        /// <summary>
        /// Whether text may wrap across multiple lines.
        /// </summary>
        public bool Wrap { get; init; }

        /// <summary>
        /// Maximum number of rendered lines. Zero means no explicit template limit.
        /// </summary>
        public int MaxLines { get; init; }

        /// <summary>
        /// Whether whitespace should be preserved instead of being normalized by the renderer.
        /// </summary>
        public bool PreserveWhitespace { get; init; }
    }

    /// <summary>
    /// Template settings for a list presenter.
    /// </summary>
    public sealed class ListTemplate {
        /// <summary>
        /// Stable template identifier referenced by controls.
        /// </summary>
        public string Id { get; init; } = string.Empty;

        /// <summary>
        /// Maximum number of visible list items before the renderer paginates or truncates.
        /// </summary>
        public int MaxVisibleItems { get; init; }

        /// <summary>
        /// Placement of auxiliary item-detail content, when the renderer supports it.
        /// </summary>
        public OverlayListDetailPlacement DetailPlacement { get; init; }
    }

    /// <summary>
    /// Template settings for a stack presenter.
    /// </summary>
    public sealed class StackTemplate {
        /// <summary>
        /// Stable template identifier referenced by controls.
        /// </summary>
        public string Id { get; init; } = string.Empty;

        /// <summary>
        /// Number of empty rows inserted between adjacent child controls.
        /// </summary>
        public int Gap { get; init; }
    }

    /// <summary>
    /// Template settings for a status-bar presenter.
    /// </summary>
    public sealed class StatusBarTemplate {
        /// <summary>
        /// Stable template identifier referenced by controls.
        /// </summary>
        public string Id { get; init; } = string.Empty;

        /// <summary>
        /// Ordered row definitions rendered by the status bar.
        /// </summary>
        public StatusBarRowTemplate[] Rows { get; init; } = [];
    }

    /// <summary>
    /// Defines a single row within a status-bar template.
    /// </summary>
    public sealed class StatusBarRowTemplate {
        /// <summary>
        /// Stable identifier for the row definition.
        /// </summary>
        public string Id { get; init; } = string.Empty;

        /// <summary>
        /// Semantic style key applied to the row container when rendered.
        /// </summary>
        public string StyleKey { get; init; } = string.Empty;

        /// <summary>
        /// Ordered field templates that compose the row.
        /// </summary>
        public StatusBarFieldTemplate[] Fields { get; init; } = [];

        /// <summary>
        /// Controls whether the row participates in full and/or compact presentations.
        /// </summary>
        public StatusRowVisibility Visibility { get; init; } = StatusRowVisibility.Always;
    }

    /// <summary>
    /// Defines how a single status field is rendered within a status-bar row.
    /// </summary>
    public sealed class StatusBarFieldTemplate {
        /// <summary>
        /// Semantic field key resolved from the local status field set.
        /// </summary>
        public string FieldKey { get; init; } = string.Empty;

        /// <summary>
        /// Semantic style key used by renderers and themes.
        /// </summary>
        public string StyleKey { get; init; } = string.Empty;

        /// <summary>
        /// Literal text inserted before the field value when rendered.
        /// </summary>
        public string PrefixText { get; init; } = string.Empty;

        /// <summary>
        /// Literal text inserted after the field value when rendered.
        /// </summary>
        public string SuffixText { get; init; } = string.Empty;

        /// <summary>
        /// Relative importance of the field in compact layouts.
        /// </summary>
        public StatusFieldCompactPriority CompactPriority { get; init; } = StatusFieldCompactPriority.Normal;

        /// <summary>
        /// Whether the field should be omitted when its resolved value is empty.
        /// </summary>
        public bool HideWhenEmpty { get; init; } = true;
    }

    /// <summary>
    /// Bundles an overlay definition and its presenter-template lookup helpers.
    /// </summary>
    public sealed class TerminalOverlay {
        /// <summary>
        /// Empty overlay used when no explicit terminal overlay has been supplied.
        /// </summary>
        public static TerminalOverlay Default { get; } = new();

        /// <summary>
        /// Stable identifier for the overlay preset.
        /// </summary>
        public string OverlayKey { get; init; } = string.Empty;

        /// <summary>
        /// Overlay and template definitions used by this overlay.
        /// </summary>
        public TerminalOverlayDefinition Definition { get; init; } = new();

        /// <summary>
        /// Finds a control by key anywhere in the recursive overlay tree.
        /// </summary>
        /// <param name="controlKey">The control key to resolve.</param>
        /// <returns>The matching control, or <see langword="null"/> when the key is empty or missing.</returns>
        public OverlayControlDefinition? FindControl(string controlKey) {
            return string.IsNullOrWhiteSpace(controlKey)
                ? null
                : FindControl(Definition.Overlay.Roots, controlKey);
        }

        /// <summary>
        /// Finds a control by semantic slot anywhere in the recursive overlay tree.
        /// </summary>
        /// <param name="semanticSlot">The semantic slot to resolve.</param>
        /// <returns>The matching control, or <see langword="null"/> when the slot is empty or missing.</returns>
        public OverlayControlDefinition? FindControlBySemanticSlot(string semanticSlot) {
            return string.IsNullOrWhiteSpace(semanticSlot)
                ? null
                : FindControlBySemanticSlot(Definition.Overlay.Roots, semanticSlot);
        }

        /// <summary>
        /// Resolves the stack template referenced by a control.
        /// </summary>
        /// <param name="control">The control whose presenter template should be resolved.</param>
        /// <returns>The matching stack template, or <see langword="null"/> when the control has no matching template.</returns>
        public StackTemplate? FindStackTemplate(OverlayControlDefinition? control) {
            return string.IsNullOrWhiteSpace(control?.PresenterTemplateId)
                ? null
                : (Definition.Templates.Stacks ?? [])
                    .FirstOrDefault(template => string.Equals(template.Id, control.PresenterTemplateId, StringComparison.Ordinal));
        }

        /// <summary>
        /// Resolves the text template referenced by a control.
        /// </summary>
        /// <param name="control">The control whose presenter template should be resolved.</param>
        /// <returns>The matching text template, or <see langword="null"/> when the control has no matching template.</returns>
        public TextTemplate? FindTextTemplate(OverlayControlDefinition? control) {
            return string.IsNullOrWhiteSpace(control?.PresenterTemplateId)
                ? null
                : (Definition.Templates.Texts ?? [])
                    .FirstOrDefault(template => string.Equals(template.Id, control.PresenterTemplateId, StringComparison.Ordinal));
        }

        /// <summary>
        /// Resolves the list template referenced by a control.
        /// </summary>
        /// <param name="control">The control whose presenter template should be resolved.</param>
        /// <returns>The matching list template, or <see langword="null"/> when the control has no matching template.</returns>
        public ListTemplate? FindListTemplate(OverlayControlDefinition? control) {
            return string.IsNullOrWhiteSpace(control?.PresenterTemplateId)
                ? null
                : (Definition.Templates.Lists ?? [])
                    .FirstOrDefault(template => string.Equals(template.Id, control.PresenterTemplateId, StringComparison.Ordinal));
        }

        /// <summary>
        /// Resolves the status-bar template referenced by a control.
        /// </summary>
        /// <param name="control">The control whose presenter template should be resolved.</param>
        /// <returns>The matching status-bar template, or <see langword="null"/> when the control has no matching template.</returns>
        public StatusBarTemplate? FindStatusBarTemplate(OverlayControlDefinition? control) {
            return string.IsNullOrWhiteSpace(control?.PresenterTemplateId)
                ? null
                : (Definition.Templates.StatusBars ?? [])
                    .FirstOrDefault(template => string.Equals(template.Id, control.PresenterTemplateId, StringComparison.Ordinal));
        }

        private static OverlayControlDefinition? FindControl(IEnumerable<OverlayControlDefinition> controls, string controlKey) {
            foreach (var control in controls) {
                if (string.Equals(control.ControlKey, controlKey, StringComparison.Ordinal)) {
                    return control;
                }

                var child = FindControl(control.Children, controlKey);
                if (child is not null) {
                    return child;
                }
            }

            return null;
        }

        private static OverlayControlDefinition? FindControlBySemanticSlot(IEnumerable<OverlayControlDefinition> controls, string semanticSlot) {
            foreach (var control in controls) {
                if (string.Equals(control.SemanticSlot, semanticSlot, StringComparison.Ordinal)) {
                    return control;
                }

                var child = FindControlBySemanticSlot(control.Children, semanticSlot);
                if (child is not null) {
                    return child;
                }
            }

            return null;
        }
    }
}
