using UnifierTSL.Contracts.Display;
using UnifierTSL.Contracts.Projection;
using UnifierTSL.Surface.Prompting.Model;
using UnifierTSL.Surface.Prompting.Runtime;
using UnifierTSL.Surface.Status;
using UnifierTSL.Contracts.Sessions;
using UnifierTSL.Contracts.Terminal;

namespace UnifierTSL.Surface.Prompting {
    public readonly record struct PromptProjectionRenderOptions(
        EditorPaneKind EditorKind,
        EditorKeymap? Keymap = null,
        EditorAuthoringBehavior? AuthoringBehavior = null,
        PromptInterpretationProjectionOverride? Interpretation = null,
        ProjectionZone CompletionZone = ProjectionZone.Support,
        ProjectionZone InterpretationZone = ProjectionZone.Context,
        bool IncludeStatus = true,
        bool SuppressCompletionPreview = false,
        CompletionActivationMode CompletionActivationMode = CompletionActivationMode.Automatic,
        ProjectionTextBlock? InputIndicator = null,
        ProjectionTextAnimation? InputIndicatorAnimation = null,
        PromptSubmitReadiness PlainEnterReadiness = PromptSubmitReadiness.UseFallback);
    public static class PromptProjectionDocumentFactory {
        // Prompt authoring should target prompt-compatible projection content directly.
        // This helper owns the shell's canonical prompt schema plus convenience wrappers,
        // but producers may extend the content with additional projection nodes.
        public static class NodeIds {
            public const string Root = "prompt.root";
            public const string Label = "prompt.label";
            public const string Input = "prompt.input";
            public const string InputIndicator = "prompt.input.indicator";
            public const string CompletionOptions = "prompt.completions";
            public const string CompletionDetail = "prompt.completion.detail";
            public const string CompletionMetadata = "prompt.completion.metadata";
            public const string InterpretationSummary = "prompt.interpretation.summary";
            public const string InterpretationOptions = "prompt.interpretations";
            public const string InterpretationDetail = "prompt.interpretation.detail";
            public const string StatusTitle = "prompt.status.title";
            public const string StatusSummary = "prompt.status.summary";
            public const string StatusDetail = "prompt.status.detail";
        }
        private static class StyleKeys {
            public const string PromptLabel = SurfaceStyleCatalog.PromptLabel;
            public const string PromptInput = SurfaceStyleCatalog.PromptInput;
            public const string CompletionBadge = SurfaceStyleCatalog.PromptCompletionBadge;
            public const string StatusBand = SurfaceStyleCatalog.StatusBand;
            public const string StatusTitle = SurfaceStyleCatalog.StatusTitle;
            public const string StatusSummary = SurfaceStyleCatalog.StatusSummary;
            public const string StatusDetail = SurfaceStyleCatalog.StatusDetail;
            public const string SyntaxKeyword = "prompt.syntax.keyword";
            public const string SyntaxModifier = "prompt.syntax.modifier";
            public const string SyntaxValue = "prompt.syntax.value";
            public const string SyntaxString = "prompt.syntax.string";
            public const string SyntaxType = "prompt.syntax.type";
            public const string SyntaxMethod = "prompt.syntax.method";
            public const string SyntaxMember = "prompt.syntax.member";
            public const string SyntaxError = "prompt.syntax.error";
            public const string GhostKeyword = "prompt.ghost.keyword";
            public const string GhostModifier = "prompt.ghost.modifier";
            public const string GhostValue = "prompt.ghost.value";
            public const string GhostString = "prompt.ghost.string";
            public const string GhostType = "prompt.ghost.type";
            public const string GhostMethod = "prompt.ghost.method";
            public const string GhostMember = "prompt.ghost.member";
            public const string GhostError = "prompt.ghost.error";
            public const string CompletionSummaryText = "prompt.completion.summary.text";
            public const string CompletionSummaryKeyword = "prompt.completion.summary.keyword";
            public const string CompletionSummaryModifier = "prompt.completion.summary.modifier";
            public const string CompletionSummaryValue = "prompt.completion.summary.value";
            public const string CompletionSummaryString = "prompt.completion.summary.string";
            public const string CompletionSummaryType = "prompt.completion.summary.type";
            public const string CompletionSummaryMethod = "prompt.completion.summary.method";
            public const string CompletionSummaryMember = "prompt.completion.summary.member";
            public const string CompletionSummaryError = "prompt.completion.summary.error";
            public const string StatusIndicator = "status.indicator";
            public const string StatusLevelPositive = "status.level.positive";
            public const string StatusLevelWarning = "status.level.warning";
            public const string StatusLevelNegative = "status.level.negative";
        }
        private static class ReasonKeys {
            public const string SubmitNotReady = "prompt.submit.not-ready";
        }
        public static ProjectionDocumentDefinition CreateDefinition(
            PromptProjectionRenderOptions options,
            IReadOnlyList<ProjectionNodeDefinition>? extraDefinitions = null,
            IReadOnlyList<ProjectionMarkerCatalogItem>? markerCatalog = null) {
            var definition = MergeDefinitions(CreateCanonicalDefinition(options), extraDefinitions);
            return new ProjectionDocumentDefinition {
                RootNodeIds = definition.RootNodeIds,
                Nodes = definition.Nodes,
                Traits = definition.Traits,
                MarkerCatalog = [.. (markerCatalog ?? [])],
            };
        }

        public static ProjectionDocumentContent CreateContent(
            PromptProjectionRenderOptions options,
            ProjectionStyleDictionary? styles = null,
            IReadOnlyList<ProjectionNodeState>? nodes = null,
            IReadOnlyList<ProjectionNodeDefinition>? extraDefinitions = null,
            ProjectionFocusState? focus = null,
            ProjectionSelectionSet? selection = null,
            IReadOnlyList<ProjectionMarkerCatalogItem>? markerCatalog = null) {
            return new ProjectionDocumentContent {
                Definition = CreateDefinition(options, extraDefinitions, markerCatalog),
                State = new ProjectionDocumentState {
                    Focus = focus ?? new ProjectionFocusState {
                        NodeId = NodeIds.Input,
                    },
                    Selection = selection ?? new ProjectionSelectionSet(),
                    Styles = ProjectionStyleDictionaryOps.WithSlots(styles ?? CreateProjectionStyleDictionary(null)),
                    Nodes = EnsureRootState(nodes),
                },
            };
        }

        public static ProjectionDocumentContent WithState(
            ProjectionDocumentContent content,
            IReadOnlyList<ProjectionNodeState>? nodes = null,
            ProjectionFocusState? focus = null,
            ProjectionSelectionSet? selection = null,
            ProjectionStyleDictionary? styles = null) {
            ArgumentNullException.ThrowIfNull(content);
            return new ProjectionDocumentContent {
                Definition = content.Definition,
                State = new ProjectionDocumentState {
                    Focus = focus ?? content.State.Focus,
                    Selection = selection ?? content.State.Selection,
                    Styles = ProjectionStyleDictionaryOps.WithSlots(styles ?? content.State.Styles),
                    Nodes = nodes is { Count: > 0 }
                        ? MergeNodeStates(content.State.Nodes, nodes)
                        : content.State.Nodes,
                },
            };
        }

        public static ProjectionDocument CreateDocument(
            InteractionScopeId scopeId,
            string interactionKind,
            ProjectionDocumentContent content) {
            ArgumentException.ThrowIfNullOrWhiteSpace(scopeId.Value);
            ArgumentException.ThrowIfNullOrWhiteSpace(interactionKind);
            ArgumentNullException.ThrowIfNull(content);
            return new ProjectionDocument {
                Scope = new ProjectionScope {
                    Kind = ProjectionScopeKind.Interaction,
                    ScopeId = scopeId.Value,
                    DocumentKind = interactionKind,
                },
                Definition = content.Definition,
                State = content.State,
            };
        }

        internal static TState? FindState<TState>(
            ProjectionDocumentContent content,
            string nodeId,
            string semanticKey = "") where TState : ProjectionNodeState {
            if (!string.IsNullOrWhiteSpace(nodeId)) {
                var direct = content.State.Nodes?
                    .OfType<TState>()
                    .FirstOrDefault(state => string.Equals(state.NodeId, nodeId, StringComparison.Ordinal));
                if (direct is not null) {
                    return direct;
                }
            }

            if (string.IsNullOrWhiteSpace(semanticKey)) {
                return null;
            }

            var stateById = (content.State.Nodes ?? [])
                .Where(static state => !string.IsNullOrWhiteSpace(state.NodeId))
                .ToDictionary(static state => state.NodeId, StringComparer.Ordinal);
            foreach (var definition in content.Definition.Nodes ?? []) {
                if (!string.Equals(definition.SemanticKey, semanticKey, StringComparison.Ordinal)
                    || !stateById.TryGetValue(definition.NodeId, out var state)
                    || state is not TState typed) {
                    continue;
                }

                return typed;
            }

            return null;
        }

        internal static string ReadText(ProjectionTextBlock? block) {
            return block?.Lines is not { Length: > 0 } lines
                ? string.Empty
                : string.Join(
                    "\n",
                    lines.Select(line => string.Concat((line.Spans ?? []).Select(static span => span.Text ?? string.Empty))));
        }

        internal static string ReadGhostText(EditableTextNodeState? inputState) {
            return inputState?.Decorations?
                .FirstOrDefault(static decoration => decoration.Kind == ProjectionInlineDecorationKind.GhostText) is { } ghost
                    ? ReadText(ghost.Content)
                    : string.Empty;
        }

        private static ProjectionDocumentDefinition CreateCanonicalDefinition(PromptProjectionRenderOptions options) {
            List<string> rootChildren = [
                NodeIds.Label,
                NodeIds.Input,
                NodeIds.InputIndicator,
                NodeIds.CompletionOptions,
                NodeIds.CompletionDetail,
                NodeIds.InterpretationSummary,
                NodeIds.InterpretationOptions,
                NodeIds.InterpretationDetail,
            ];
            if (options.IncludeStatus) {
                rootChildren.AddRange([
                    NodeIds.StatusTitle,
                    NodeIds.StatusSummary,
                    NodeIds.StatusDetail,
                ]);
            }

            List<ProjectionNodeDefinition> nodes = [
                new ProjectionNodeDefinition {
                    NodeId = NodeIds.Root,
                    Kind = ProjectionNodeKind.Container,
                    ChildNodeIds = [.. rootChildren],
                },
                new ProjectionNodeDefinition {
                    NodeId = NodeIds.Label,
                    Kind = ProjectionNodeKind.Text,
                    Role = ProjectionSemanticRole.Label,
                    SemanticKey = EditorProjectionSemanticKeys.InputLabel,
                    Zone = ProjectionZone.Support,
                    Bindings = CreateBinding(ProjectionNodeBindingKind.LabelFor, NodeIds.Input),
                    Traits = new ProjectionNodeTraits {
                        HideWhenEmpty = true,
                    },
                },
                new ProjectionNodeDefinition {
                    NodeId = NodeIds.Input,
                    Kind = ProjectionNodeKind.EditableText,
                    Role = ProjectionSemanticRole.Input,
                    SemanticKey = EditorProjectionSemanticKeys.Input,
                    Zone = ProjectionZone.Primary,
                    Traits = new ProjectionNodeTraits {
                        IsFocusable = true,
                        IsInteractive = true,
                        Input = CreateInputPolicy(options),
                    },
                },
                new ProjectionNodeDefinition {
                    NodeId = NodeIds.InputIndicator,
                    Kind = ProjectionNodeKind.Text,
                    Role = ProjectionSemanticRole.Feedback,
                    SemanticKey = EditorProjectionSemanticKeys.InputIndicator,
                    Zone = ProjectionZone.Support,
                    Traits = new ProjectionNodeTraits {
                        HideWhenEmpty = true,
                    },
                },
                new ProjectionNodeDefinition {
                    NodeId = NodeIds.CompletionOptions,
                    Kind = ProjectionNodeKind.Collection,
                    Role = ProjectionSemanticRole.Options,
                    SemanticKey = EditorProjectionSemanticKeys.AssistPrimaryList,
                    Zone = options.CompletionZone,
                    Traits = new ProjectionNodeTraits {
                        HideWhenEmpty = true,
                        IsInteractive = true,
                        CollectionHint = ProjectionCollectionPresentationHint.List,
                    },
                },
                new ProjectionNodeDefinition {
                    NodeId = NodeIds.CompletionDetail,
                    Kind = ProjectionNodeKind.Detail,
                    Role = ProjectionSemanticRole.Detail,
                    SemanticKey = EditorProjectionSemanticKeys.AssistPrimaryDetail,
                    Zone = ProjectionZone.Detail,
                    Bindings = CreateBinding(ProjectionNodeBindingKind.ActiveItemSource, NodeIds.CompletionOptions),
                    Traits = new ProjectionNodeTraits {
                        HideWhenEmpty = true,
                    },
                },
                new ProjectionNodeDefinition {
                    NodeId = NodeIds.CompletionMetadata,
                    Kind = ProjectionNodeKind.PropertySet,
                    Role = ProjectionSemanticRole.Metadata,
                    SemanticKey = EditorProjectionSemanticKeys.AssistPrimaryMetadata,
                    Zone = ProjectionZone.Background,
                    Traits = new ProjectionNodeTraits {
                        HideWhenEmpty = true,
                    },
                },
                new ProjectionNodeDefinition {
                    NodeId = NodeIds.InterpretationSummary,
                    Kind = ProjectionNodeKind.Text,
                    Role = ProjectionSemanticRole.Summary,
                    SemanticKey = EditorProjectionSemanticKeys.AssistSecondarySummary,
                    Zone = ProjectionZone.Support,
                    Traits = new ProjectionNodeTraits {
                        HideWhenEmpty = true,
                    },
                },
                new ProjectionNodeDefinition {
                    NodeId = NodeIds.InterpretationOptions,
                    Kind = ProjectionNodeKind.Collection,
                    Role = ProjectionSemanticRole.Options,
                    SemanticKey = EditorProjectionSemanticKeys.AssistSecondaryList,
                    Zone = options.InterpretationZone,
                    Traits = new ProjectionNodeTraits {
                        HideWhenEmpty = true,
                        IsInteractive = true,
                        CollectionHint = ProjectionCollectionPresentationHint.Inline,
                    },
                },
                new ProjectionNodeDefinition {
                    NodeId = NodeIds.InterpretationDetail,
                    Kind = ProjectionNodeKind.Detail,
                    Role = ProjectionSemanticRole.Detail,
                    SemanticKey = EditorProjectionSemanticKeys.AssistSecondaryDetail,
                    Zone = ProjectionZone.Detail,
                    Bindings = CreateBinding(ProjectionNodeBindingKind.ActiveItemSource, NodeIds.InterpretationOptions),
                    Traits = CreateInterpretationDetailTraits(options),
                },
            ];
            if (options.IncludeStatus) {
                nodes.AddRange([
                    new ProjectionNodeDefinition {
                        NodeId = NodeIds.StatusTitle,
                        Kind = ProjectionNodeKind.Text,
                        Role = ProjectionSemanticRole.Label,
                        SemanticKey = EditorProjectionSemanticKeys.StatusHeader,
                        Zone = ProjectionZone.Support,
                        Traits = new ProjectionNodeTraits {
                            HideWhenEmpty = true,
                        },
                    },
                    new ProjectionNodeDefinition {
                        NodeId = NodeIds.StatusSummary,
                        Kind = ProjectionNodeKind.Text,
                        Role = ProjectionSemanticRole.Feedback,
                        SemanticKey = EditorProjectionSemanticKeys.StatusSummary,
                        Zone = ProjectionZone.Support,
                        Traits = new ProjectionNodeTraits {
                            HideWhenEmpty = true,
                        },
                    },
                    new ProjectionNodeDefinition {
                        NodeId = NodeIds.StatusDetail,
                        Kind = ProjectionNodeKind.Detail,
                        Role = ProjectionSemanticRole.Detail,
                        SemanticKey = EditorProjectionSemanticKeys.StatusDetail,
                        Zone = ProjectionZone.Detail,
                        Traits = new ProjectionNodeTraits {
                            HideWhenEmpty = true,
                            IsInteractive = true,
                        },
                    },
                ]);
            }

            return new ProjectionDocumentDefinition {
                RootNodeIds = [NodeIds.Root],
                Nodes = [.. nodes],
            };
        }

        public static ProjectionStyleDictionary CreateProjectionStyleDictionary(StyleDictionary? source) {
            var resolvedSource = StyleDictionaryOps.Merge(
                PromptStyleKeys.Default,
                SurfaceStyleCatalog.Default,
                source);
            Dictionary<string, ProjectionStyleDefinition> stylesById = new(StringComparer.Ordinal);
            foreach (var style in resolvedSource.Styles ?? []) {
                if (style is null || string.IsNullOrWhiteSpace(style.StyleId)) {
                    continue;
                }

                var styleKey = ResolveStyleKey(style.StyleId);
                stylesById[styleKey] = new ProjectionStyleDefinition {
                    Key = styleKey,
                    Foreground = CloneColor(style.Foreground),
                    Background = CloneColor(style.Background),
                    TextAttributes = CloneTextAttributes(style.TextAttributes),
                };
            }

            if (!stylesById.ContainsKey(StyleKeys.PromptLabel)
                && stylesById.TryGetValue(SurfaceStyleCatalog.Accent, out var accentStyle)) {
                stylesById[StyleKeys.PromptLabel] = new ProjectionStyleDefinition {
                    Key = StyleKeys.PromptLabel,
                    Foreground = accentStyle.Foreground,
                    Background = accentStyle.Background,
                    TextAttributes = accentStyle.TextAttributes,
                };
            }

            return ProjectionStyleDictionaryOps.WithSlots([.. stylesById.Values]);
        }

        public static ProjectionStyleDictionary MergeStyles(params ProjectionStyleDictionary?[] dictionaries) {
            Dictionary<string, ProjectionStyleDefinition> stylesById = new(StringComparer.Ordinal);
            foreach (var dictionary in dictionaries ?? []) {
                foreach (var style in dictionary?.Styles ?? []) {
                    if (style is null || string.IsNullOrWhiteSpace(style.Key)) {
                        continue;
                    }

                    stylesById[style.Key] = style;
                }
            }

            return ProjectionStyleDictionaryOps.WithSlots([.. stylesById.Values]);
        }

        internal static ProjectionDocumentContent CreatePublishedContent(
            ProjectionDocumentContent authoredContent,
            PromptComputation computation,
            PromptCandidateWindowState candidateWindow,
            PromptInputState inputState,
            PromptBufferedAuthoringOptions bufferedAuthoring,
            long bufferRevision = 0) {
            return CreatePublishedContent(
                authoredContent,
                computation,
                candidateWindow,
                inputState,
                CreateRenderOptions(
                    bufferedAuthoring,
                    PromptCanonicalSurfaceSupport.CreateInterpretationProjectionOverride(computation.InterpretationState)),
                bufferRevision);
        }

        internal static PromptProjectionRenderOptions CreateRenderOptions(
            PromptBufferedAuthoringOptions bufferedAuthoring,
            PromptInterpretationProjectionOverride? interpretation = null) {
            return new PromptProjectionRenderOptions(
                bufferedAuthoring.EditorKind,
                bufferedAuthoring.Keymap,
                bufferedAuthoring.AuthoringBehavior,
                Interpretation: interpretation,
                PlainEnterReadiness: bufferedAuthoring.PlainEnterReadiness);
        }

        public static ProjectionSubmitState CreateSubmitState(
            EmptySubmitBehavior emptySubmitBehavior = EmptySubmitBehavior.KeepInput,
            bool ctrlEnterBypassesGhost = true,
            PromptSubmitReadiness plainEnterReadiness = PromptSubmitReadiness.UseFallback,
            bool acceptsSubmit = true,
            string? notReadyReason = null) {
            return new ProjectionSubmitState {
                AcceptsSubmit = acceptsSubmit,
                IsReady = plainEnterReadiness == PromptSubmitReadiness.Ready,
                Reason = plainEnterReadiness == PromptSubmitReadiness.NotReady
                    ? string.IsNullOrWhiteSpace(notReadyReason)
                        ? ReasonKeys.SubmitNotReady
                        : notReadyReason
                    : string.Empty,
                EmptyInputAction = emptySubmitBehavior == EmptySubmitBehavior.AcceptGhostIfAvailable
                    ? ProjectionEmptySubmitAction.AcceptPreviewIfAvailable
                    : ProjectionEmptySubmitAction.KeepBuffer,
                AlternateSubmitBypassesPreview = ctrlEnterBypassesGhost,
            };
        }

        private static PropertySetNodeState CreateCompletionMetadataState(PromptProjectionRenderOptions options) {
            return new PropertySetNodeState {
                Properties = [
                    new ProjectionPropertyValue {
                        PropertyKey = EditorProjectionMetadataKeys.AssistPrimaryActivationMode,
                        Value = CreateSingleLineBlock(
                            options.CompletionActivationMode == CompletionActivationMode.Automatic
                                ? "automatic"
                                : "manual",
                            string.Empty),
                    },
                ],
            };
        }

        internal static ProjectionDocumentContent CreatePublishedContent(
            ProjectionDocumentContent authoredContent,
            PromptComputation computation,
            PromptCandidateWindowState candidateWindow,
            PromptInputState inputState,
            PromptProjectionRenderOptions options,
            long bufferRevision = 0) {
            var normalizedInputState = inputState.CopyNormalized();
            var authoredInput = FindState<EditableTextProjectionNodeState>(authoredContent, NodeIds.Input);
            var authoredStatusTitle = FindState<TextProjectionNodeState>(authoredContent, NodeIds.StatusTitle);
            var authoredCompletionMetadata = FindState<PropertySetProjectionNodeState>(authoredContent, NodeIds.CompletionMetadata);
            var submitBehavior = CreateSubmitBehavior(authoredInput?.State.Submit);
            var editorMaterial = PromptCanonicalSurfaceSupport.CreateEditorMaterialState(
                authoredContent,
                computation,
                candidateWindow,
                normalizedInputState,
                submitBehavior,
                options.SuppressCompletionPreview);
            string sourceText = normalizedInputState.InputText ?? string.Empty;
            var displayedCandidates = PromptCandidateWindowProjector.ResolveDisplayedCandidates(computation, candidateWindow);
            ProjectionCollectionItem[] completionItems =
                [.. displayedCandidates.Select(candidate => CreateCompletionItem(candidate, sourceText))];
            int selectedCompletionIndex = candidateWindow.IsPaged
                ? normalizedInputState.CompletionIndex > 0
                    ? normalizedInputState.CompletionIndex - Math.Max(0, candidateWindow.WindowOffset) - 1
                    : -1
                : normalizedInputState.CompletionIndex > 0
                    ? Math.Clamp(normalizedInputState.CompletionIndex - 1, 0, Math.Max(0, completionItems.Length - 1))
                    : -1;
            if (selectedCompletionIndex < 0 || selectedCompletionIndex >= completionItems.Length) {
                selectedCompletionIndex = -1;
            }

            int totalCompletionCount = candidateWindow.IsPaged
                ? Math.Max(0, candidateWindow.TotalCandidateCount)
                : completionItems.Length;
            int completionWindowOffset = Math.Max(0, candidateWindow.WindowOffset);
            int completionPageSize = candidateWindow.IsPaged
                ? Math.Max(1, candidateWindow.PageSize)
                : completionItems.Length;
            var interpretationState = options.Interpretation ?? PromptCanonicalSurfaceSupport.CreateInterpretationProjectionOverride(computation.InterpretationState);
            var completionSelection = ResolveCompletionSelection(completionItems, selectedCompletionIndex);
            var selectedCompletionItemId = ResolveSelectedCompletionItemId(completionItems, selectedCompletionIndex);
            List<ProjectionNodeState> nodes = [
                CreateInputNode(authoredInput, editorMaterial, normalizedInputState, bufferRevision, options),
                new CollectionProjectionNodeState {
                    NodeId = NodeIds.CompletionOptions,
                    State = new CollectionNodeState {
                        Items = completionItems,
                        TotalItemCount = totalCompletionCount,
                        WindowOffset = completionWindowOffset,
                        PageSize = completionPageSize,
                        IsPaged = candidateWindow.IsPaged,
                    },
                },
                new DetailProjectionNodeState {
                    NodeId = NodeIds.CompletionDetail,
                    State = new DetailNodeState {
                        ContextItemId = selectedCompletionItemId,
                        Lines = ResolveCompletionDetail(completionItems, selectedCompletionIndex),
                    },
                },
                CreateCompletionMetadataNode(authoredCompletionMetadata, options),
                new TextProjectionNodeState {
                    NodeId = NodeIds.InterpretationSummary,
                    State = new TextNodeState {
                        Content = ResolveInterpretationSummary(interpretationState),
                    },
                },
                new CollectionProjectionNodeState {
                    NodeId = NodeIds.InterpretationOptions,
                    State = new CollectionNodeState {
                        Items = [.. interpretationState.Options.Select(option => new ProjectionCollectionItem {
                            ItemId = option.Id,
                            Label = ToBlock(option.Label),
                        })],
                        TotalItemCount = (interpretationState.Options ?? []).Length,
                    },
                },
                new DetailProjectionNodeState {
                    NodeId = NodeIds.InterpretationDetail,
                    State = new DetailNodeState {
                        ContextItemId = interpretationState.ActiveInterpretationId,
                        Lines = ToBlocks(interpretationState.DetailLines),
                    },
                },
            ];
            if (options.InputIndicator is not null || options.InputIndicatorAnimation is not null) {
                nodes.Add(new TextProjectionNodeState {
                    NodeId = NodeIds.InputIndicator,
                    State = new TextNodeState {
                        Content = options.InputIndicator ?? new ProjectionTextBlock(),
                        Animation = options.InputIndicatorAnimation,
                    },
                });
            }

            if (options.IncludeStatus) {
                nodes.Add(CreateStatusTitleNode(authoredStatusTitle));
                nodes.Add(new DetailProjectionNodeState {
                    NodeId = NodeIds.StatusDetail,
                    State = new DetailNodeState {
                        Lines = CreateBlocks(computation.StatusBodyLines, SurfaceStyleCatalog.StatusDetail),
                    },
                });
            }

            return WithState(
                authoredContent,
                nodes: nodes,
                focus: ResolveFocus(authoredContent.State.Focus, new ProjectionFocusState {
                    NodeId = NodeIds.Input,
                }),
                selection: MergeSelections(authoredContent.State.Selection, completionSelection),
                styles: MergeStyles(CreateProjectionStyleDictionary(null), authoredContent.State.Styles));
        }

        private static ProjectionFocusState ResolveFocus(ProjectionFocusState authoredFocus, ProjectionFocusState fallbackFocus) {
            return !string.IsNullOrWhiteSpace(authoredFocus?.NodeId)
                || !string.IsNullOrWhiteSpace(authoredFocus?.ItemId)
                ? authoredFocus
                : fallbackFocus;
        }

        private static ProjectionSelectionSet MergeSelections(
            ProjectionSelectionSet authoredSelection,
            ProjectionSelectionSet overlaySelection) {
            var order = new List<string>();
            Dictionary<string, ProjectionNodeSelectionState> selectionsByNodeId = new(StringComparer.Ordinal);
            foreach (var selection in authoredSelection?.Nodes ?? []) {
                if (selection is null || string.IsNullOrWhiteSpace(selection.NodeId)) {
                    continue;
                }

                order.Add(selection.NodeId);
                selectionsByNodeId[selection.NodeId] = selection;
            }

            foreach (var selection in overlaySelection?.Nodes ?? []) {
                if (selection is null || string.IsNullOrWhiteSpace(selection.NodeId)) {
                    continue;
                }

                if (!selectionsByNodeId.ContainsKey(selection.NodeId)) {
                    order.Add(selection.NodeId);
                }

                selectionsByNodeId[selection.NodeId] = selection;
            }

            return new ProjectionSelectionSet {
                Nodes = [.. order.Select(nodeId => selectionsByNodeId[nodeId])],
            };
        }

        private static EditableTextProjectionNodeState CreateInputNode(
            EditableTextProjectionNodeState? authoredState,
            EditorMaterialState editorMaterial,
            PromptInputState inputState,
            long expectedClientBufferRevision,
            PromptProjectionRenderOptions options) {
            return new EditableTextProjectionNodeState {
                NodeId = NodeIds.Input,
                State = new EditableTextNodeState {
                    BufferText = inputState.InputText ?? string.Empty,
                    CaretIndex = inputState.CursorIndex,
                    ExpectedClientBufferRevision = Math.Max(0, expectedClientBufferRevision),
                    Markers = [.. (authoredState?.State.Markers ?? [])],
                    Decorations = MergeInputDecorations(
                        authoredState?.State.Decorations,
                        editorMaterial.Content,
                        editorMaterial.GhostHint),
                    Submit = CreatePublishedSubmitState(authoredState?.State.Submit, options),
                },
            };
        }

        private static EditorSubmitBehavior CreateSubmitBehavior(ProjectionSubmitState? submit) {
            var authoredSubmit = submit ?? new ProjectionSubmitState();
            return new EditorSubmitBehavior {
                EmptyInputAction = authoredSubmit.EmptyInputAction == ProjectionEmptySubmitAction.AcceptPreviewIfAvailable
                    ? EmptyInputSubmitAction.AcceptPreviewIfAvailable
                    : EmptyInputSubmitAction.KeepInput,
                CtrlEnterBypassesPreview = authoredSubmit.AlternateSubmitBypassesPreview,
                PlainEnterReadiness = ToSubmitReadiness(ResolveSubmitReadiness(authoredSubmit)),
            };
        }

        private static ProjectionSubmitState CreatePublishedSubmitState(
            ProjectionSubmitState? authoredSubmit,
            PromptProjectionRenderOptions options) {
            var resolvedSubmit = authoredSubmit ?? CreateSubmitState(plainEnterReadiness: options.PlainEnterReadiness);
            var readiness = ResolveSubmitReadiness(resolvedSubmit);
            return CreateSubmitState(
                resolvedSubmit.EmptyInputAction == ProjectionEmptySubmitAction.AcceptPreviewIfAvailable
                    ? EmptySubmitBehavior.AcceptGhostIfAvailable
                    : EmptySubmitBehavior.KeepInput,
                resolvedSubmit.AlternateSubmitBypassesPreview,
                readiness,
                resolvedSubmit.AcceptsSubmit,
                resolvedSubmit.Reason);
        }

        private static PromptSubmitReadiness ResolveSubmitReadiness(ProjectionSubmitState submit) {
            if (!submit.AcceptsSubmit) {
                return PromptSubmitReadiness.NotReady;
            }

            if (submit.IsReady) {
                return PromptSubmitReadiness.Ready;
            }

            return string.IsNullOrWhiteSpace(submit.Reason)
                ? PromptSubmitReadiness.UseFallback
                : PromptSubmitReadiness.NotReady;
        }

        private static SubmitReadiness ToSubmitReadiness(PromptSubmitReadiness readiness) {
            return readiness switch {
                PromptSubmitReadiness.Ready => SubmitReadiness.Ready,
                PromptSubmitReadiness.NotReady => SubmitReadiness.NotReady,
                _ => SubmitReadiness.UseFallback,
            };
        }

        private static ProjectionInlineDecoration[] MergeInputDecorations(
            IReadOnlyList<ProjectionInlineDecoration>? authoredDecorations,
            InlineSegments content,
            GhostInlineHint ghostHint) {
            List<ProjectionInlineDecoration> decorations = [];
            foreach (var decoration in authoredDecorations ?? []) {
                if (decoration is null
                    || decoration.Kind is ProjectionInlineDecorationKind.GhostText or ProjectionInlineDecorationKind.Highlight) {
                    continue;
                }

                decorations.Add(decoration);
            }

            decorations.AddRange(ToDecorations(content));
            decorations.AddRange(ToDecorations(ghostHint));
            return [.. decorations];
        }

        private static PropertySetProjectionNodeState CreateCompletionMetadataNode(
            PropertySetProjectionNodeState? authoredState,
            PromptProjectionRenderOptions options) {
            var metadata = CreateCompletionMetadataState(options);
            return new PropertySetProjectionNodeState {
                NodeId = NodeIds.CompletionMetadata,
                State = new PropertySetNodeState {
                    Properties = MergeProperties(authoredState?.State.Properties, metadata.Properties),
                },
            };
        }

        private static TextProjectionNodeState CreateStatusTitleNode(TextProjectionNodeState? authoredState) {
            if (authoredState is not null && (HasTextContent(authoredState.State.Content) || authoredState.State.Animation is not null)) {
                return authoredState;
            }

            return new TextProjectionNodeState {
                NodeId = NodeIds.StatusTitle,
                State = new TextNodeState {
                    Content = CreateSingleLineBlock(
                        StatusProjectionDocumentFactory.TitleText,
                        SurfaceStyleCatalog.StatusTitle),
                },
            };
        }

        private static ProjectionPropertyValue[] MergeProperties(
            IReadOnlyList<ProjectionPropertyValue>? authoredProperties,
            IReadOnlyList<ProjectionPropertyValue>? overlayProperties) {
            var order = new List<string>();
            Dictionary<string, ProjectionPropertyValue> propertiesByKey = new(StringComparer.Ordinal);
            foreach (var property in authoredProperties ?? []) {
                if (property is null || string.IsNullOrWhiteSpace(property.PropertyKey)) {
                    continue;
                }

                order.Add(property.PropertyKey);
                propertiesByKey[property.PropertyKey] = property;
            }

            foreach (var property in overlayProperties ?? []) {
                if (property is null || string.IsNullOrWhiteSpace(property.PropertyKey)) {
                    continue;
                }

                if (!propertiesByKey.ContainsKey(property.PropertyKey)) {
                    order.Add(property.PropertyKey);
                }

                propertiesByKey[property.PropertyKey] = property;
            }

            return [.. order.Select(propertyKey => propertiesByKey[propertyKey])];
        }

        private static bool HasTextContent(ProjectionTextBlock? block) {
            return block?.Lines is { Length: > 0 } lines
                && lines.Any(line => (line.Spans ?? []).Any(static span => !string.IsNullOrEmpty(span.Text)));
        }

        private static ProjectionDocumentDefinition MergeDefinitions(
            ProjectionDocumentDefinition canonicalDefinition,
            IReadOnlyList<ProjectionNodeDefinition>? extraDefinitions) {
            if (extraDefinitions is not { Count: > 0 }) {
                return canonicalDefinition;
            }

            var order = new List<string>();
            Dictionary<string, ProjectionNodeDefinition> definitionsById = new(StringComparer.Ordinal);
            foreach (var definition in canonicalDefinition.Nodes ?? []) {
                if (definition is null || string.IsNullOrWhiteSpace(definition.NodeId)) {
                    continue;
                }

                order.Add(definition.NodeId);
                definitionsById[definition.NodeId] = definition;
            }

            foreach (var definition in extraDefinitions) {
                if (definition is null || string.IsNullOrWhiteSpace(definition.NodeId)) {
                    continue;
                }

                if (!definitionsById.ContainsKey(definition.NodeId)) {
                    order.Add(definition.NodeId);
                }

                definitionsById[definition.NodeId] = definition;
            }

            return new ProjectionDocumentDefinition {
                RootNodeIds = canonicalDefinition.RootNodeIds,
                Traits = canonicalDefinition.Traits,
                Nodes = [.. order.Select(nodeId => definitionsById[nodeId])],
            };
        }

        private static ProjectionNodeState[] EnsureRootState(IReadOnlyList<ProjectionNodeState>? nodes) {
            var merged = MergeNodeStates([], nodes);
            return merged.Any(static node => string.Equals(node.NodeId, NodeIds.Root, StringComparison.Ordinal))
                ? merged
                : [new ContainerProjectionNodeState {
                    NodeId = NodeIds.Root,
                    State = new ContainerNodeState(),
                }, .. merged];
        }

        private static ProjectionNodeState[] MergeNodeStates(
            IReadOnlyList<ProjectionNodeState>? baseline,
            IReadOnlyList<ProjectionNodeState>? replacements) {
            var order = new List<string>();
            Dictionary<string, ProjectionNodeState> statesById = new(StringComparer.Ordinal);
            foreach (var state in baseline ?? []) {
                if (state is null || string.IsNullOrWhiteSpace(state.NodeId)) {
                    continue;
                }

                order.Add(state.NodeId);
                statesById[state.NodeId] = state;
            }

            foreach (var state in replacements ?? []) {
                if (state is null || string.IsNullOrWhiteSpace(state.NodeId)) {
                    continue;
                }

                if (!statesById.ContainsKey(state.NodeId)) {
                    order.Add(state.NodeId);
                }

                statesById[state.NodeId] = state;
            }

            return [.. order.Select(nodeId => statesById[nodeId])];
        }

        private static ProjectionNodeTraits CreateInterpretationDetailTraits(PromptProjectionRenderOptions options) {
            return new ProjectionNodeTraits {
                HideWhenEmpty = true,
                ExpansionHint = options.Interpretation?.Presentation?.PrefersExpandedDetail == true
                    ? ProjectionExpansionHint.Expanded
                    : ProjectionExpansionHint.Default,
            };
        }

        private static ProjectionInputPolicy CreateInputPolicy(PromptProjectionRenderOptions options) {
            var keymap = options.Keymap ?? new EditorKeymap();
            var authoring = options.AuthoringBehavior ?? new EditorAuthoringBehavior();
            List<ProjectionInputBinding> bindings = [];
            bindings.AddRange(CreateCommandBindings(keymap.Submit, ProjectionInputCommandKind.Submit));
            bindings.AddRange(CreateCommandBindings(keymap.AltSubmit, ProjectionInputCommandKind.AlternateSubmit));
            bindings.AddRange(CreateCommandBindings(keymap.NewLine, ProjectionInputCommandKind.InsertNewLine));
            bindings.AddRange(CreateCommandBindings(keymap.DismissAssist, ProjectionInputCommandKind.DismissAssist));
            bindings.AddRange(CreateCommandBindings(keymap.PrevActivity, ProjectionInputCommandKind.PreviousContextItem));
            bindings.AddRange(CreateCommandBindings(keymap.NextActivity, ProjectionInputCommandKind.NextContextItem));
            bindings.AddRange(CreateActionBindings(keymap.ManualCompletion, EditorProjectionActionIds.ActivateCompletion));
            bindings.AddRange(CreateActionBindings(keymap.AcceptCompletion, EditorProjectionActionIds.AcceptCompletion));
            bindings.AddRange(CreateActionBindings(keymap.AcceptPreview, EditorProjectionActionIds.AcceptPreview));
            bindings.AddRange(CreateActionBindings(keymap.NextCompletion, EditorProjectionActionIds.NextCompletion));
            bindings.AddRange(CreateActionBindings(keymap.PrevCompletion, EditorProjectionActionIds.PreviousCompletion));
            bindings.AddRange(CreateActionBindings(keymap.NextInterpretation, EditorProjectionActionIds.NextInterpretation));
            bindings.AddRange(CreateActionBindings(keymap.PrevInterpretation, EditorProjectionActionIds.PreviousInterpretation));
            if (options.IncludeStatus) {
                bindings.AddRange(CreateActionBindings(keymap.ScrollStatusUp, EditorProjectionActionIds.ScrollStatusBackward));
                bindings.AddRange(CreateActionBindings(keymap.ScrollStatusDown, EditorProjectionActionIds.ScrollStatusForward));
            }

            return new ProjectionInputPolicy {
                Kind = ToInputKind(options.EditorKind),
                Authority = ProjectionInputAuthority.ClientBuffered,
                Bindings = [.. bindings],
                Authoring = new ProjectionAuthoringPolicy {
                    OpensCompletionAutomatically = authoring.OpensCompletionAutomatically,
                    CapturesRawKeys = authoring.CapturesRawKeys,
                    MultilineSubmitMode = authoring.MultilineSubmitMode == MultilineSubmitMode.UseReadiness
                        ? ProjectionMultilineSubmitMode.UseReadiness
                        : ProjectionMultilineSubmitMode.AlwaysSubmit,
                },
            };
        }

        private static ProjectionInputKind ToInputKind(EditorPaneKind editorKind) {
            return editorKind switch {
                EditorPaneKind.MultiLine => ProjectionInputKind.MultiLine,
                EditorPaneKind.ReadonlyBuffer => ProjectionInputKind.ReadonlyBuffer,
                _ => ProjectionInputKind.SingleLine,
            };
        }

        private static IEnumerable<ProjectionInputBinding> CreateCommandBindings(
            IReadOnlyList<KeyChord>? chords,
            ProjectionInputCommandKind command) {
            return (chords ?? [])
                .Select(chord => new ProjectionCommandInputBinding {
                    Gesture = CreateGesture(chord),
                    Command = command,
                });
        }

        private static IEnumerable<ProjectionInputBinding> CreateActionBindings(
            IReadOnlyList<KeyChord>? chords,
            string actionId) {
            return (chords ?? [])
                .Select(chord => new ProjectionActionInputBinding {
                    Gesture = CreateGesture(chord),
                    ActionId = actionId,
                });
        }

        private static ProjectionKeyboardGesture CreateGesture(KeyChord chord) {
            return new ProjectionKeyboardGesture {
                Key = NormalizeKey(chord.Key),
                Shift = (chord.Modifiers & ConsoleModifiers.Shift) != 0,
                Alt = (chord.Modifiers & ConsoleModifiers.Alt) != 0,
                Control = (chord.Modifiers & ConsoleModifiers.Control) != 0,
            };
        }

        private static string NormalizeKey(ConsoleKey key) {
            return key switch {
                ConsoleKey.Enter => "enter",
                ConsoleKey.Tab => "tab",
                ConsoleKey.RightArrow => "arrow-right",
                ConsoleKey.LeftArrow => "arrow-left",
                ConsoleKey.UpArrow => "arrow-up",
                ConsoleKey.DownArrow => "arrow-down",
                ConsoleKey.Escape => "escape",
                ConsoleKey.Spacebar => "space",
                _ when key >= ConsoleKey.A && key <= ConsoleKey.Z => ((char)('a' + (key - ConsoleKey.A))).ToString(),
                _ when key >= ConsoleKey.D0 && key <= ConsoleKey.D9 => ((char)('0' + (key - ConsoleKey.D0))).ToString(),
                _ => throw new NotSupportedException(
                    GetString($"Prompt projection does not define a neutral keyboard gesture mapping for console key '{key}'.")),
            };
        }

        private static ProjectionNodeBindingSet CreateBinding(ProjectionNodeBindingKind kind, string targetNodeId) {
            return new ProjectionNodeBindingSet {
                Links = [
                    new ProjectionNodeBinding {
                        Kind = kind,
                        TargetNodeId = targetNodeId,
                    },
                ],
            };
        }

        private static ProjectionSelectionSet ResolveCompletionSelection(ProjectionCollectionItem[] items, int selectedItemIndex) {
            var itemId = ResolveSelectedCompletionItemId(items, selectedItemIndex);
            return string.IsNullOrWhiteSpace(itemId)
                ? new ProjectionSelectionSet()
                : new ProjectionSelectionSet {
                    Nodes = [
                        new ProjectionNodeSelectionState {
                            NodeId = NodeIds.CompletionOptions,
                            ActiveItemId = itemId,
                            SelectedItemIds = [itemId],
                        },
                    ],
                };
        }

        private static ProjectionCollectionItem CreateCompletionItem(PromptCompletionItem item, string sourceText) {
            return new ProjectionCollectionItem {
                ItemId = ResolveCompletionId(item, sourceText),
                Label = ToBlock(PromptCanonicalSurfaceSupport.CreateStyledSegment(
                    item.DisplayText,
                    item.DisplayHighlights,
                    item.DisplayStyleId)),
                SecondaryLabel = ToBlock(PromptCanonicalSurfaceSupport.CreateStyledSegment(
                    item.SecondaryDisplayText,
                    item.SecondaryDisplayHighlights,
                    item.SecondaryDisplayStyleId)),
                TrailingLabel = ToBlock(PromptCanonicalSurfaceSupport.CreateStyledSegment(
                    item.TrailingDisplayText,
                    item.TrailingDisplayHighlights,
                    item.TrailingDisplayStyleId)),
                Summary = ToBlock(PromptCanonicalSurfaceSupport.CreateStyledSegment(
                    item.SummaryText,
                    item.SummaryHighlights,
                    item.SummaryStyleId)),
                PrimaryEdit = new ProjectionTextEditOperation {
                    TargetNodeId = NodeIds.Input,
                    StartIndex = item.PrimaryEdit.StartIndex,
                    Length = item.PrimaryEdit.Length,
                    NewText = item.PrimaryEdit.NewText ?? string.Empty,
                },
            };
        }

        private static string ResolveCompletionId(PromptCompletionItem item, string sourceText) {
            if (!string.IsNullOrWhiteSpace(item.Id)) {
                return item.Id;
            }

            string resolved = item.PrimaryEdit.Apply(sourceText);
            return string.IsNullOrWhiteSpace(resolved)
                ? item.DisplayText ?? string.Empty
                : resolved;
        }

        private static string ResolveSelectedCompletionItemId(ProjectionCollectionItem[] items, int selectedItemIndex) {
            return selectedItemIndex >= 0 && selectedItemIndex < items.Length
                ? items[selectedItemIndex].ItemId ?? string.Empty
                : string.Empty;
        }

        private static ProjectionTextBlock[] ResolveCompletionDetail(
            ProjectionCollectionItem[] items,
            int selectedItemIndex) {
            if (selectedItemIndex < 0 || selectedItemIndex >= items.Length) {
                return [];
            }

            var summary = items[selectedItemIndex].Summary;
            return StatusProjectionDocumentFactory.HasVisibleText(summary) ? [summary] : [];
        }

        internal static ProjectionTextBlock ResolveInterpretationSummary(PromptInterpretationProjectionOverride interpretationState) {
            List<ProjectionTextLine> lines = [];
            if (InlineSegmentsOps.HasVisibleText(interpretationState.Summary)) {
                lines.AddRange(ToLines(interpretationState.Summary));
            }

            return new ProjectionTextBlock {
                Lines = [.. lines],
            };
        }

        private static ProjectionInlineDecoration[] ToDecorations(InlineSegments segments) {
            return [.. (segments.Highlights ?? [])
                .Where(static highlight => highlight.Length > 0)
                .Select(highlight => new ProjectionInlineDecoration {
                    Kind = ProjectionInlineDecorationKind.Highlight,
                    StartIndex = highlight.StartIndex,
                    Length = highlight.Length,
                    Style = ProjectionStyleDictionaryOps.Reference(ResolveStyleKey(highlight.StyleId)),
                    Content = new ProjectionTextBlock(),
                })];
        }

        private static ProjectionInlineDecoration[] ToDecorations(GhostInlineHint ghostHint) {
            return [.. (ghostHint.Insertions ?? [])
                .Where(static insertion => InlineSegmentsOps.HasVisibleText(insertion.Content))
                .Select(insertion => new ProjectionInlineDecoration {
                    Kind = ProjectionInlineDecorationKind.GhostText,
                    StartIndex = Math.Max(0, insertion.SourceIndex),
                    Content = ToBlock(insertion.Content),
                })];
        }

        internal static ProjectionTextBlock ToBlock(InlineSegments? segments) {
            if (!InlineSegmentsOps.HasVisibleText(segments)) {
                return new ProjectionTextBlock();
            }

            return new ProjectionTextBlock {
                Lines = [.. ToLines(segments!)],
            };
        }

        private static ProjectionTextBlock ToBlock(StyledTextLine? line) {
            return line is null || !StyledTextLineOps.HasVisibleText(line)
                ? new ProjectionTextBlock()
                : new ProjectionTextBlock {
                    Lines = [
                        new ProjectionTextLine {
                            Style = ProjectionStyleDictionaryOps.Reference(ResolveStyleKey(line.LineStyleId)),
                            Spans = [.. (line.Runs ?? [])
                                .Where(static run => !string.IsNullOrEmpty(run.Text))
                                .Select(run => new ProjectionTextSpan {
                                    Text = run.Text ?? string.Empty,
                                    Style = ProjectionStyleDictionaryOps.Reference(ResolveStyleKey(run.StyleId)),
                                })],
                        },
                    ],
                };
        }

        internal static ProjectionTextBlock[] ToBlocks(IReadOnlyList<InlineSegments>? lines) {
            return lines is not { Count: > 0 }
                ? []
                : [.. lines.Select(ToBlock)];
        }

        private static ProjectionTextBlock[] ToBlocks(IReadOnlyList<StyledTextLine>? lines) {
            return lines is not { Count: > 0 }
                ? []
                : [.. lines.Select(ToBlock)];
        }

        public static ProjectionTextBlock CreateSingleLineBlock(string? text, string styleKey = "") {
            return string.IsNullOrEmpty(text)
                ? new ProjectionTextBlock()
                : new ProjectionTextBlock {
                    Lines = [
                        new ProjectionTextLine {
                            Spans = [
                                new ProjectionTextSpan {
                                    Text = text,
                                    Style = ProjectionStyleDictionaryOps.Reference(styleKey),
                                },
                            ],
                        },
                    ],
                };
        }

        public static ProjectionTextBlock[] CreateBlocks(IEnumerable<string>? lines, string styleKey = "") {
            return lines is null
                ? []
                : [.. lines
                    .Where(static line => !string.IsNullOrWhiteSpace(line))
                    .Select(line => CreateSingleLineBlock(line, styleKey))];
        }

        private static IEnumerable<ProjectionTextLine> ToLines(InlineSegments segments) {
            if (!InlineSegmentsOps.HasVisibleText(segments)) {
                return [];
            }

            var text = segments.Text ?? string.Empty;
            var styleIds = new string[text.Length];
            foreach (var highlight in segments.Highlights ?? []) {
                if (highlight.Length <= 0) {
                    continue;
                }

                var start = Math.Clamp(highlight.StartIndex, 0, text.Length);
                var end = Math.Clamp(highlight.EndIndex, start, text.Length);
                for (var index = start; index < end; index++) {
                    styleIds[index] = highlight.StyleId ?? string.Empty;
                }
            }

            List<ProjectionTextLine> lines = [];
            var lineStart = 0;
            for (var index = 0; index <= text.Length; index++) {
                var reachedEnd = index == text.Length;
                if (!reachedEnd && text[index] != '\n') {
                    continue;
                }

                lines.Add(new ProjectionTextLine {
                    Spans = [.. CreateSpans(text, styleIds, lineStart, index)],
                });
                lineStart = index + 1;
            }

            return lines;
        }

        private static IEnumerable<ProjectionTextSpan> CreateSpans(
            string text,
            string[] styleIds,
            int start,
            int end) {
            if (end <= start) {
                return [];
            }

            List<ProjectionTextSpan> spans = [];
            var runStart = start;
            var activeStyleId = styleIds[start];
            for (var index = start + 1; index <= end; index++) {
                var reachedEnd = index == end;
                var styleChanged = !reachedEnd && !string.Equals(styleIds[index], activeStyleId, StringComparison.Ordinal);
                if (!reachedEnd && !styleChanged) {
                    continue;
                }

                var value = text[runStart..index];
                if (value.Length > 0) {
                    spans.Add(new ProjectionTextSpan {
                        Text = value,
                        Style = ProjectionStyleDictionaryOps.Reference(ResolveStyleKey(activeStyleId)),
                    });
                }

                if (!reachedEnd) {
                    runStart = index;
                    activeStyleId = styleIds[index];
                }
            }

            return spans;
        }

        private static string ResolveStyleKey(string? styleId) {
            return styleId switch {
                PromptStyleKeys.SyntaxKeyword => StyleKeys.SyntaxKeyword,
                PromptStyleKeys.SyntaxModifier => StyleKeys.SyntaxModifier,
                PromptStyleKeys.SyntaxValue => StyleKeys.SyntaxValue,
                PromptStyleKeys.SyntaxError => StyleKeys.SyntaxError,
                PromptStyleKeys.GhostKeyword => StyleKeys.GhostKeyword,
                PromptStyleKeys.GhostModifier => StyleKeys.GhostModifier,
                PromptStyleKeys.GhostValue => StyleKeys.GhostValue,
                PromptStyleKeys.GhostError => StyleKeys.GhostError,
                PromptStyleKeys.CompletionSummaryText => StyleKeys.CompletionSummaryText,
                PromptStyleKeys.CompletionSummaryKeyword => StyleKeys.CompletionSummaryKeyword,
                PromptStyleKeys.CompletionSummaryModifier => StyleKeys.CompletionSummaryModifier,
                PromptStyleKeys.CompletionSummaryValue => StyleKeys.CompletionSummaryValue,
                PromptStyleKeys.CompletionSummaryError => StyleKeys.CompletionSummaryError,
                SurfaceStyleCatalog.PromptLabel => StyleKeys.PromptLabel,
                SurfaceStyleCatalog.PromptInput => StyleKeys.PromptInput,
                SurfaceStyleCatalog.PromptCompletionBadge => StyleKeys.CompletionBadge,
                SurfaceStyleCatalog.StatusBand => StyleKeys.StatusBand,
                SurfaceStyleCatalog.StatusTitle => StyleKeys.StatusTitle,
                SurfaceStyleCatalog.StatusSummary => StyleKeys.StatusSummary,
                SurfaceStyleCatalog.StatusDetail => StyleKeys.StatusDetail,
                SurfaceStyleCatalog.InlineHint => StyleKeys.GhostValue,
                SurfaceStyleCatalog.Accent => SurfaceStyleCatalog.Accent,
                SurfaceStyleCatalog.Positive => SurfaceStyleCatalog.Positive,
                SurfaceStyleCatalog.Warning => SurfaceStyleCatalog.Warning,
                SurfaceStyleCatalog.Negative => SurfaceStyleCatalog.Negative,
                SurfaceStyleCatalog.StatusHeaderIndicator => StyleKeys.StatusIndicator,
                SurfaceStyleCatalog.StatusHeaderPositive => StyleKeys.StatusLevelPositive,
                SurfaceStyleCatalog.StatusHeaderWarning => StyleKeys.StatusLevelWarning,
                SurfaceStyleCatalog.StatusHeaderNegative => StyleKeys.StatusLevelNegative,
                _ => string.IsNullOrWhiteSpace(styleId) ? string.Empty : styleId,
            };
        }

        private static ProjectionColorValue? CloneColor(StyledColorValue? color) {
            return color is null
                ? null
                : new ProjectionColorValue {
                    Red = color.Red,
                    Green = color.Green,
                    Blue = color.Blue,
                };
        }

        private static ProjectionTextAttributes CloneTextAttributes(StyledTextAttributes attributes) {
            return (ProjectionTextAttributes)(byte)attributes;
        }
    }
}
