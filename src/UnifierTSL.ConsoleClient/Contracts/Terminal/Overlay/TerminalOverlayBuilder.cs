namespace UnifierTSL.Contracts.Terminal.Overlay {
    /// <summary>
    /// Authoring helper that builds <see cref="TerminalOverlay"/> instances without requiring
    /// callers to manually assign presenter-template identifiers.
    /// </summary>
    public sealed class TerminalOverlayBuilder {
        private readonly List<TextTemplate> textTemplates = [];
        private readonly List<ListTemplate> listTemplates = [];
        private readonly List<StackTemplate> stackTemplates = [];
        private readonly List<StatusBarTemplate> statusBarTemplates = [];
        private readonly Dictionary<object, string> templateIds = new(ReferenceEqualityComparer.Instance);

        private int nextTextTemplateId = 1;
        private int nextListTemplateId = 1;
        private int nextStackTemplateId = 1;
        private int nextStatusBarTemplateId = 1;

        /// <summary>
        /// Stable identifier for the overlay preset.
        /// </summary>
        public string OverlayKey { get; init; } = string.Empty;

        /// <summary>
        /// Creates a text control and registers its template, if any.
        /// </summary>
        public OverlayControlDefinition Text(
            string controlKey,
            OverlayInteractionLayer interactionLayer,
            OverlayAnchorTarget anchorTarget,
            TextTemplate? template = null,
            string semanticSlot = "",
            int minHorizontalViewportMargin = 0) {
            return new OverlayControlDefinition {
                ControlKey = controlKey,
                SemanticSlot = semanticSlot,
                Kind = OverlayControlKind.Text,
                InteractionLayer = interactionLayer,
                AnchorTarget = anchorTarget,
                MinHorizontalViewportMargin = minHorizontalViewportMargin,
                PresenterTemplateId = RegisterTextTemplate(template),
            };
        }

        /// <summary>
        /// Creates a list control and registers its template, if any.
        /// </summary>
        public OverlayControlDefinition List(
            string controlKey,
            OverlayInteractionLayer interactionLayer,
            OverlayAnchorTarget anchorTarget,
            ListTemplate? template = null,
            string semanticSlot = "",
            int minHorizontalViewportMargin = 0) {
            return new OverlayControlDefinition {
                ControlKey = controlKey,
                SemanticSlot = semanticSlot,
                Kind = OverlayControlKind.List,
                InteractionLayer = interactionLayer,
                AnchorTarget = anchorTarget,
                MinHorizontalViewportMargin = minHorizontalViewportMargin,
                PresenterTemplateId = RegisterListTemplate(template),
            };
        }

        /// <summary>
        /// Creates a status-bar control and registers its template, if any.
        /// </summary>
        public OverlayControlDefinition StatusBar(
            string controlKey,
            OverlayInteractionLayer interactionLayer,
            OverlayAnchorTarget anchorTarget,
            StatusBarTemplate? template = null,
            string semanticSlot = "",
            int minHorizontalViewportMargin = 0) {
            return new OverlayControlDefinition {
                ControlKey = controlKey,
                SemanticSlot = semanticSlot,
                Kind = OverlayControlKind.StatusBar,
                InteractionLayer = interactionLayer,
                AnchorTarget = anchorTarget,
                MinHorizontalViewportMargin = minHorizontalViewportMargin,
                PresenterTemplateId = RegisterStatusBarTemplate(template),
            };
        }

        /// <summary>
        /// Creates a stack control and registers its template, if any.
        /// </summary>
        public OverlayControlDefinition Stack(
            string controlKey,
            OverlayInteractionLayer interactionLayer,
            OverlayAnchorTarget anchorTarget,
            StackTemplate? template = null,
            OverlayWidthPolicy widthPolicy = OverlayWidthPolicy.FillViewportWidth,
            OverlayOverflowPolicy overflowPolicy = OverlayOverflowPolicy.PreferThenClamp,
            string semanticSlot = "",
            int minHorizontalViewportMargin = 0,
            params OverlayControlDefinition[] children) {
            return new OverlayControlDefinition {
                ControlKey = controlKey,
                SemanticSlot = semanticSlot,
                Kind = OverlayControlKind.Stack,
                InteractionLayer = interactionLayer,
                AnchorTarget = anchorTarget,
                WidthPolicy = widthPolicy,
                OverflowPolicy = overflowPolicy,
                MinHorizontalViewportMargin = minHorizontalViewportMargin,
                PresenterTemplateId = RegisterStackTemplate(template),
                Children = children ?? [],
            };
        }

        /// <summary>
        /// Produces an overlay using the controls and templates registered through this builder.
        /// </summary>
        public TerminalOverlay Build(params OverlayControlDefinition[] roots) {
            return new TerminalOverlay {
                OverlayKey = OverlayKey,
                Definition = new TerminalOverlayDefinition {
                    Overlay = new OverlayRootDefinition {
                        Roots = roots ?? [],
                    },
                    Templates = new SurfaceTemplateDictionary {
                        Texts = [.. textTemplates],
                        Lists = [.. listTemplates],
                        Stacks = [.. stackTemplates],
                        StatusBars = [.. statusBarTemplates],
                    },
                },
            };
        }

        private string RegisterTextTemplate(TextTemplate? template) {
            return RegisterTemplate(
                template,
                textTemplates,
                ref nextTextTemplateId,
                "text",
                static (source, id) => new TextTemplate {
                    Id = id,
                    Wrap = source.Wrap,
                    MaxLines = source.MaxLines,
                    PreserveWhitespace = source.PreserveWhitespace,
                });
        }

        private string RegisterListTemplate(ListTemplate? template) {
            return RegisterTemplate(
                template,
                listTemplates,
                ref nextListTemplateId,
                "list",
                static (source, id) => new ListTemplate {
                    Id = id,
                    MaxVisibleItems = source.MaxVisibleItems,
                    DetailPlacement = source.DetailPlacement,
                });
        }

        private string RegisterStackTemplate(StackTemplate? template) {
            return RegisterTemplate(
                template,
                stackTemplates,
                ref nextStackTemplateId,
                "stack",
                static (source, id) => new StackTemplate {
                    Id = id,
                    Gap = source.Gap,
                });
        }

        private string RegisterStatusBarTemplate(StatusBarTemplate? template) {
            return RegisterTemplate(
                template,
                statusBarTemplates,
                ref nextStatusBarTemplateId,
                "statusbar",
                static (source, id) => new StatusBarTemplate {
                    Id = id,
                    Rows = [.. (source.Rows ?? []).Select(static row => new StatusBarRowTemplate {
                        Id = row.Id,
                        StyleKey = row.StyleKey,
                        Visibility = row.Visibility,
                        Fields = [.. (row.Fields ?? []).Select(static field => new StatusBarFieldTemplate {
                            FieldKey = field.FieldKey,
                            StyleKey = field.StyleKey,
                            PrefixText = field.PrefixText,
                            SuffixText = field.SuffixText,
                            CompactPriority = field.CompactPriority,
                            HideWhenEmpty = field.HideWhenEmpty,
                        })],
                    })],
                });
        }

        private string RegisterTemplate<TTemplate>(
            TTemplate? template,
            List<TTemplate> registeredTemplates,
            ref int nextTemplateId,
            string templateKind,
            Func<TTemplate, string, TTemplate> clone)
            where TTemplate : class {
            if (template is null) {
                return string.Empty;
            }

            if (templateIds.TryGetValue(template, out var existingId)) {
                return existingId;
            }

            var templateId = $"__auto.{templateKind}.{nextTemplateId++}";
            templateIds.Add(template, templateId);
            registeredTemplates.Add(clone(template, templateId));
            return templateId;
        }
    }
}
