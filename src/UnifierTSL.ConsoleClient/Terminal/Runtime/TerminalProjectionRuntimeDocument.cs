using UnifierTSL.Contracts.Display;
using UnifierTSL.Contracts.Projection;
using UnifierTSL.Contracts.Sessions;
using UnifierTSL.Contracts.Terminal;

namespace UnifierTSL.Terminal.Runtime {
    internal readonly record struct ProjectionContainerNodeRuntime(
        ProjectionNodeDefinition Definition,
        ContainerNodeState State);

    internal readonly record struct ProjectionTextNodeRuntime(
        ProjectionNodeDefinition Definition,
        TextNodeState State);

    internal readonly record struct ProjectionEditableNodeRuntime(
        ProjectionNodeDefinition Definition,
        EditableTextNodeState State);

    internal readonly record struct ProjectionCollectionNodeRuntime(
        ProjectionNodeDefinition Definition,
        CollectionNodeState State);

    internal readonly record struct ProjectionPropertySetNodeRuntime(
        ProjectionNodeDefinition Definition,
        PropertySetNodeState State);

    internal readonly record struct ProjectionDetailNodeRuntime(
        ProjectionNodeDefinition Definition,
        DetailNodeState State);

    internal sealed class TerminalProjectionRuntimeDocument {
        public static TerminalProjectionRuntimeDocument Empty { get; } = new(null);

        private readonly ProjectionDocument? document;
        private readonly IReadOnlyDictionary<string, ProjectionNodeDefinition> definitionsByNodeId;
        private readonly IReadOnlyDictionary<string, ProjectionNodeSelectionState> selectionsByNodeId;
        private readonly IReadOnlyDictionary<string, ContainerProjectionNodeState> containerStates;
        private readonly IReadOnlyDictionary<string, TextProjectionNodeState> textStates;
        private readonly IReadOnlyDictionary<string, EditableTextProjectionNodeState> editableStates;
        private readonly IReadOnlyDictionary<string, CollectionProjectionNodeState> collectionStates;
        private readonly IReadOnlyDictionary<string, PropertySetProjectionNodeState> propertySetStates;
        private readonly IReadOnlyDictionary<string, DetailProjectionNodeState> detailStates;
        private readonly IReadOnlyDictionary<string, ProjectionNodeDefinition> textDefinitionsBySemanticKey;
        private readonly IReadOnlyDictionary<string, ProjectionNodeDefinition> editableDefinitionsBySemanticKey;
        private readonly IReadOnlyDictionary<string, ProjectionNodeDefinition> collectionDefinitionsBySemanticKey;
        private readonly IReadOnlyDictionary<string, ProjectionNodeDefinition> propertySetDefinitionsBySemanticKey;
        private readonly IReadOnlyDictionary<string, ProjectionNodeDefinition> detailDefinitionsBySemanticKey;

        private TerminalProjectionRuntimeDocument(ProjectionDocument? document) {
            this.document = document;
            definitionsByNodeId = (document?.Definition.Nodes ?? [])
                .ToDictionary(static node => node.NodeId, StringComparer.Ordinal);
            selectionsByNodeId = (document?.State.Selection.Nodes ?? [])
                .ToDictionary(static node => node.NodeId, StringComparer.Ordinal);
            containerStates = (document?.State.Nodes ?? [])
                .OfType<ContainerProjectionNodeState>()
                .ToDictionary(static node => node.NodeId, StringComparer.Ordinal);
            textStates = (document?.State.Nodes ?? [])
                .OfType<TextProjectionNodeState>()
                .ToDictionary(static node => node.NodeId, StringComparer.Ordinal);
            editableStates = (document?.State.Nodes ?? [])
                .OfType<EditableTextProjectionNodeState>()
                .ToDictionary(static node => node.NodeId, StringComparer.Ordinal);
            collectionStates = (document?.State.Nodes ?? [])
                .OfType<CollectionProjectionNodeState>()
                .ToDictionary(static node => node.NodeId, StringComparer.Ordinal);
            propertySetStates = (document?.State.Nodes ?? [])
                .OfType<PropertySetProjectionNodeState>()
                .ToDictionary(static node => node.NodeId, StringComparer.Ordinal);
            detailStates = (document?.State.Nodes ?? [])
                .OfType<DetailProjectionNodeState>()
                .ToDictionary(static node => node.NodeId, StringComparer.Ordinal);
            textDefinitionsBySemanticKey = BuildSemanticIndex(ProjectionNodeKind.Text);
            editableDefinitionsBySemanticKey = BuildSemanticIndex(ProjectionNodeKind.EditableText);
            collectionDefinitionsBySemanticKey = BuildSemanticIndex(ProjectionNodeKind.Collection);
            propertySetDefinitionsBySemanticKey = BuildSemanticIndex(ProjectionNodeKind.PropertySet);
            detailDefinitionsBySemanticKey = BuildSemanticIndex(ProjectionNodeKind.Detail);
        }

        public ProjectionDocument? Document => document;
        public ProjectionDocumentDefinition Definition => document?.Definition ?? new ProjectionDocumentDefinition();
        public ProjectionDocumentState State => document?.State ?? new ProjectionDocumentState();
        public ProjectionSelectionSet Selection => State.Selection;
        public bool HasDocument => document is not null;
        public TerminalSurfaceInteractionMode InteractionMode => editableStates.Count > 0
            ? TerminalSurfaceInteractionMode.Editor
            : TerminalSurfaceInteractionMode.Display;

        public static TerminalProjectionRuntimeDocument Create(ProjectionDocument? document) {
            return document is null ? Empty : new TerminalProjectionRuntimeDocument(document);
        }

        public EditorPaneRuntimeState CreateEditorPane() {
            if (InteractionMode == TerminalSurfaceInteractionMode.Display) {
                return new EditorPaneRuntimeState {
                    Kind = EditorPaneKind.ReadonlyBuffer,
                    Authority = EditorAuthority.Readonly,
                    AcceptsSubmit = false,
                };
            }

            var input = FindInput()
                ?? throw new InvalidOperationException("Interaction projection document does not define an editable input node.");
            var inputPolicy = input.Definition.Traits.Input ?? new ProjectionInputPolicy();
            var bufferText = input.State.BufferText ?? string.Empty;
            return new EditorPaneRuntimeState {
                Kind = ToEditorPaneKind(inputPolicy.Kind),
                Authority = ToEditorAuthority(inputPolicy.Authority),
                BufferText = bufferText,
                CaretIndex = Math.Clamp(input.State.CaretIndex, 0, bufferText.Length),
                ExpectedClientBufferRevision = Math.Max(0, input.State.ExpectedClientBufferRevision),
                RemoteRevision = Math.Max(0, input.State.RemoteRevision),
                Markers = [.. (input.State.Markers ?? []).Select(static marker => new ClientBufferedTextMarker {
                    Key = marker.Key ?? string.Empty,
                    VariantKey = marker.VariantKey ?? string.Empty,
                    StartIndex = Math.Max(0, marker.StartIndex),
                    Length = Math.Max(0, marker.Length),
                })],
                MarkerCatalog = [.. (Definition.MarkerCatalog ?? []).Select(static marker => new ProjectionMarkerCatalogItem {
                    Key = marker.Key ?? string.Empty,
                    VariantKey = marker.VariantKey ?? string.Empty,
                    DisplayText = marker.DisplayText ?? string.Empty,
                    Style = marker.Style,
                })],
                AcceptsSubmit = input.State.Submit.AcceptsSubmit,
                Keymap = CreateGenericKeymap(inputPolicy),
                AuthoringBehavior = CreateEditorAuthoringBehavior(inputPolicy.Authoring),
            };
        }

        public ProjectionContainerNodeRuntime? FindContainerByNodeId(string nodeId) {
            return TryGetNode(
                nodeId,
                containerStates,
                static (definition, state) => new ProjectionContainerNodeRuntime(definition, state.State));
        }

        public ProjectionTextNodeRuntime? FindTextByNodeId(string nodeId) {
            return TryGetNode(
                nodeId,
                textStates,
                static (definition, state) => new ProjectionTextNodeRuntime(definition, state.State));
        }

        public ProjectionEditableNodeRuntime? FindEditableByNodeId(string nodeId) {
            return TryGetNode(
                nodeId,
                editableStates,
                static (definition, state) => new ProjectionEditableNodeRuntime(definition, state.State));
        }

        public ProjectionCollectionNodeRuntime? FindCollectionByNodeId(string nodeId) {
            return TryGetNode(
                nodeId,
                collectionStates,
                static (definition, state) => new ProjectionCollectionNodeRuntime(definition, state.State));
        }

        public ProjectionPropertySetNodeRuntime? FindPropertySetByNodeId(string nodeId) {
            return TryGetNode(
                nodeId,
                propertySetStates,
                static (definition, state) => new ProjectionPropertySetNodeRuntime(definition, state.State));
        }

        public ProjectionDetailNodeRuntime? FindDetailByNodeId(string nodeId) {
            return TryGetNode(
                nodeId,
                detailStates,
                static (definition, state) => new ProjectionDetailNodeRuntime(definition, state.State));
        }

        public ProjectionTextNodeRuntime? FindTextBySemanticKey(string semanticKey) {
            return TryGetNode(
                semanticKey,
                textDefinitionsBySemanticKey,
                textStates,
                static (definition, state) => new ProjectionTextNodeRuntime(definition, state.State));
        }

        public ProjectionEditableNodeRuntime? FindEditableBySemanticKey(string semanticKey) {
            return TryGetNode(
                semanticKey,
                editableDefinitionsBySemanticKey,
                editableStates,
                static (definition, state) => new ProjectionEditableNodeRuntime(definition, state.State));
        }

        public ProjectionCollectionNodeRuntime? FindCollectionBySemanticKey(string semanticKey) {
            return TryGetNode(
                semanticKey,
                collectionDefinitionsBySemanticKey,
                collectionStates,
                static (definition, state) => new ProjectionCollectionNodeRuntime(definition, state.State));
        }

        public ProjectionPropertySetNodeRuntime? FindPropertySetBySemanticKey(string semanticKey) {
            return TryGetNode(
                semanticKey,
                propertySetDefinitionsBySemanticKey,
                propertySetStates,
                static (definition, state) => new ProjectionPropertySetNodeRuntime(definition, state.State));
        }

        public ProjectionDetailNodeRuntime? FindDetailBySemanticKey(string semanticKey) {
            return TryGetNode(
                semanticKey,
                detailDefinitionsBySemanticKey,
                detailStates,
                static (definition, state) => new ProjectionDetailNodeRuntime(definition, state.State));
        }

        public ProjectionEditableNodeRuntime? FindInput() {
            var definition = FindDefinitionBySemanticKey(editableDefinitionsBySemanticKey, EditorProjectionSemanticKeys.Input)
                ?? definitionsByNodeId.Values.FirstOrDefault(node => node.Kind == ProjectionNodeKind.EditableText && node.Role == ProjectionSemanticRole.Input)
                ?? definitionsByNodeId.Values.FirstOrDefault(node => node.Kind == ProjectionNodeKind.EditableText);
            return definition is null || !editableStates.TryGetValue(definition.NodeId, out var state)
                ? null
                : new ProjectionEditableNodeRuntime(definition, state.State);
        }

        public ProjectionTextNodeRuntime? FindInputLabel(string inputNodeId) {
            return FindText(
                EditorProjectionSemanticKeys.InputLabel,
                node => node.Role == ProjectionSemanticRole.Label && HasBinding(node, ProjectionNodeBindingKind.LabelFor, inputNodeId));
        }

        public ProjectionTextNodeRuntime? FindText(
            string semanticKey,
            Func<ProjectionNodeDefinition, bool> fallbackPredicate) {
            return FindTextBySemanticKey(semanticKey)
                ?? FindNode(
                    textStates,
                    fallbackPredicate,
                    static (definition, state) => new ProjectionTextNodeRuntime(definition, state.State));
        }

        public ProjectionDetailNodeRuntime? FindDetail(
            string semanticKey,
            Func<ProjectionNodeDefinition, bool> fallbackPredicate) {
            return FindDetailBySemanticKey(semanticKey)
                ?? FindNode(
                    detailStates,
                    fallbackPredicate,
                    static (definition, state) => new ProjectionDetailNodeRuntime(definition, state.State));
        }

        public ProjectionDetailNodeRuntime? FindBoundDetail(string sourceNodeId) {
            return FindNode(
                detailStates,
                node => HasBinding(node, ProjectionNodeBindingKind.ActiveItemSource, sourceNodeId),
                static (definition, state) => new ProjectionDetailNodeRuntime(definition, state.State));
        }

        public ProjectionNodeSelectionState? FindSelection(string nodeId) {
            return string.IsNullOrWhiteSpace(nodeId)
                ? selectionsByNodeId.Values.FirstOrDefault(static selection => !string.IsNullOrWhiteSpace(selection.ActiveItemId))
                : selectionsByNodeId.TryGetValue(nodeId, out var selection)
                    ? selection
                    : null;
        }

        public string ResolveMetadataValue(ProjectionPropertySetNodeRuntime? propertySet, string propertyKey) {
            var property = (propertySet?.State.Properties ?? [])
                .FirstOrDefault(item => string.Equals(item.PropertyKey, propertyKey, StringComparison.Ordinal));
            return StyledTextLineOps.ToPlainText(ProjectionStyledTextAdapter.ToStyledFirstLine(property?.Value));
        }

        public bool HasBinding(
            ProjectionNodeDefinition definition,
            ProjectionNodeBindingKind kind,
            string? targetNodeId = null) {
            return (definition.Bindings.Links ?? []).Any(link =>
                link.Kind == kind
                && (targetNodeId is null || string.Equals(link.TargetNodeId, targetNodeId, StringComparison.Ordinal)));
        }

        public bool UsesPopupAssist() {
            return IsPopupAssistZone(FindCollectionBySemanticKey(EditorProjectionSemanticKeys.AssistPrimaryList)?.Definition.Zone)
                || IsPopupAssistZone(FindCollectionBySemanticKey(EditorProjectionSemanticKeys.AssistSecondaryList)?.Definition.Zone);
        }

        public bool SupportsInteractionStatus() {
            return textDefinitionsBySemanticKey.ContainsKey(EditorProjectionSemanticKeys.StatusHeader)
                || textDefinitionsBySemanticKey.ContainsKey(EditorProjectionSemanticKeys.StatusSummary)
                || detailDefinitionsBySemanticKey.ContainsKey(EditorProjectionSemanticKeys.StatusDetail);
        }

        public bool IsContainerVisible(string nodeId) {
            return !containerStates.TryGetValue(nodeId, out var state) || state.State.IsVisible;
        }

        public TerminalAnimatedText ResolveAnimatedText(ProjectionTextNodeRuntime? text) {
            return TerminalAnimatedTextAdapter.Create(text?.State);
        }

        public string ResolveActiveItemId(
            ProjectionCollectionNodeRuntime? collection,
            ProjectionDetailNodeRuntime? detail) {
            var selected = collection is { } collectionValue
                ? FindSelection(collectionValue.Definition.NodeId)
                : null;
            return !string.IsNullOrWhiteSpace(selected?.ActiveItemId)
                ? selected.ActiveItemId
                : detail?.State.ContextItemId ?? string.Empty;
        }

        public int ResolveItemIndex(IReadOnlyList<ProjectionCollectionItem>? items, string? itemId) {
            if (items is not { Count: > 0 } || string.IsNullOrWhiteSpace(itemId)) {
                return -1;
            }

            for (var index = 0; index < items.Count; index++) {
                if (string.Equals(items[index].ItemId, itemId, StringComparison.Ordinal)) {
                    return index;
                }
            }

            return -1;
        }

        private IReadOnlyDictionary<string, ProjectionNodeDefinition> BuildSemanticIndex(ProjectionNodeKind kind) {
            Dictionary<string, ProjectionNodeDefinition> index = new(StringComparer.Ordinal);
            foreach (var definition in definitionsByNodeId.Values) {
                if (definition.Kind != kind || string.IsNullOrWhiteSpace(definition.SemanticKey) || index.ContainsKey(definition.SemanticKey)) {
                    continue;
                }

                index[definition.SemanticKey] = definition;
            }

            return index;
        }

        private static bool IsPopupAssistZone(ProjectionZone? zone) {
            return zone is ProjectionZone.Auxiliary or ProjectionZone.Modal;
        }

        private ProjectionNodeDefinition? FindDefinitionBySemanticKey(
            IReadOnlyDictionary<string, ProjectionNodeDefinition> definitionsBySemanticKey,
            string semanticKey) {
            return string.IsNullOrWhiteSpace(semanticKey)
                ? null
                : definitionsBySemanticKey.TryGetValue(semanticKey, out var definition)
                    ? definition
                    : null;
        }

        private TResult? FindNode<TState, TResult>(
            IReadOnlyDictionary<string, TState> statesByNodeId,
            Func<ProjectionNodeDefinition, bool> predicate,
            Func<ProjectionNodeDefinition, TState, TResult> materialize)
            where TState : ProjectionNodeState
            where TResult : struct {
            foreach (var definition in definitionsByNodeId.Values.Where(predicate)) {
                if (statesByNodeId.TryGetValue(definition.NodeId, out var state)) {
                    return materialize(definition, state);
                }
            }

            return null;
        }

        private TResult? TryGetNode<TState, TResult>(
            string nodeId,
            IReadOnlyDictionary<string, TState> statesByNodeId,
            Func<ProjectionNodeDefinition, TState, TResult> materialize)
            where TState : ProjectionNodeState
            where TResult : struct {
            return definitionsByNodeId.TryGetValue(nodeId, out var definition)
                && statesByNodeId.TryGetValue(nodeId, out var state)
                    ? materialize(definition, state)
                    : null;
        }

        private static TResult? TryGetNode<TState, TResult>(
            string semanticKey,
            IReadOnlyDictionary<string, ProjectionNodeDefinition> definitionsBySemanticKey,
            IReadOnlyDictionary<string, TState> statesByNodeId,
            Func<ProjectionNodeDefinition, TState, TResult> materialize)
            where TState : ProjectionNodeState
            where TResult : struct {
            return !string.IsNullOrWhiteSpace(semanticKey)
                && definitionsBySemanticKey.TryGetValue(semanticKey, out var definition)
                && statesByNodeId.TryGetValue(definition.NodeId, out var state)
                    ? materialize(definition, state)
                    : null;
        }

        private static EditorAuthoringBehavior CreateEditorAuthoringBehavior(ProjectionAuthoringPolicy? authoring) {
            authoring ??= new ProjectionAuthoringPolicy();
            return new EditorAuthoringBehavior {
                OpensCompletionAutomatically = authoring.OpensCompletionAutomatically,
                CapturesRawKeys = authoring.CapturesRawKeys,
                MultilineSubmitMode = authoring.MultilineSubmitMode == ProjectionMultilineSubmitMode.UseReadiness
                    ? MultilineSubmitMode.UseReadiness
                    : MultilineSubmitMode.AlwaysSubmit,
            };
        }

        private static EditorKeymap CreateGenericKeymap(ProjectionInputPolicy inputPolicy) {
            List<KeyChord> submit = [];
            List<KeyChord> altSubmit = [];
            List<KeyChord> newLine = [];
            List<KeyChord> manualCompletion = [];
            List<KeyChord> acceptCompletion = [];
            List<KeyChord> acceptPreview = [];
            List<KeyChord> nextCompletion = [];
            List<KeyChord> prevCompletion = [];
            List<KeyChord> dismissAssist = [];
            List<KeyChord> nextInterpretation = [];
            List<KeyChord> prevInterpretation = [];
            List<KeyChord> prevActivity = [];
            List<KeyChord> nextActivity = [];
            List<KeyChord> scrollStatusUp = [];
            List<KeyChord> scrollStatusDown = [];
            foreach (var binding in inputPolicy.Bindings ?? []) {
                var chord = ToKeyChord(binding.Gesture);
                switch (binding) {
                    case ProjectionCommandInputBinding commandBinding:
                        AddCommandBinding(
                            commandBinding.Command,
                            chord,
                            submit,
                            altSubmit,
                            newLine,
                            dismissAssist,
                            prevActivity,
                            nextActivity);
                        break;

                    case ProjectionActionInputBinding actionBinding:
                        switch (actionBinding.ActionId) {
                            case EditorProjectionActionIds.ActivateCompletion:
                                manualCompletion.Add(chord);
                                break;
                            case EditorProjectionActionIds.AcceptCompletion:
                                acceptCompletion.Add(chord);
                                break;
                            case EditorProjectionActionIds.AcceptPreview:
                                acceptPreview.Add(chord);
                                break;
                            case EditorProjectionActionIds.NextCompletion:
                                nextCompletion.Add(chord);
                                break;
                            case EditorProjectionActionIds.PreviousCompletion:
                                prevCompletion.Add(chord);
                                break;
                            case EditorProjectionActionIds.NextInterpretation:
                                nextInterpretation.Add(chord);
                                break;
                            case EditorProjectionActionIds.PreviousInterpretation:
                                prevInterpretation.Add(chord);
                                break;
                            case EditorProjectionActionIds.ScrollStatusBackward:
                                scrollStatusUp.Add(chord);
                                break;
                            case EditorProjectionActionIds.ScrollStatusForward:
                                scrollStatusDown.Add(chord);
                                break;
                        }
                        break;
                }
            }

            return new EditorKeymap {
                DispatchPolicy = EditorKeyDispatchPolicy.Standard,
                Submit = [.. submit],
                AltSubmit = [.. altSubmit],
                NewLine = [.. newLine],
                ManualCompletion = [.. manualCompletion],
                AcceptCompletion = [.. acceptCompletion],
                AcceptPreview = [.. acceptPreview],
                NextCompletion = [.. nextCompletion],
                PrevCompletion = [.. prevCompletion],
                DismissAssist = [.. dismissAssist],
                NextInterpretation = [.. nextInterpretation],
                PrevInterpretation = [.. prevInterpretation],
                PrevActivity = [.. prevActivity],
                NextActivity = [.. nextActivity],
                ScrollStatusUp = [.. scrollStatusUp],
                ScrollStatusDown = [.. scrollStatusDown],
            };
        }

        private static void AddCommandBinding(
            ProjectionInputCommandKind command,
            KeyChord chord,
            ICollection<KeyChord> submit,
            ICollection<KeyChord> altSubmit,
            ICollection<KeyChord> newLine,
            ICollection<KeyChord> dismissAssist,
            ICollection<KeyChord> prevContextItem,
            ICollection<KeyChord> nextContextItem) {
            switch (command) {
                case ProjectionInputCommandKind.Submit:
                    submit.Add(chord);
                    break;
                case ProjectionInputCommandKind.AlternateSubmit:
                    altSubmit.Add(chord);
                    break;
                case ProjectionInputCommandKind.InsertNewLine:
                    newLine.Add(chord);
                    break;
                case ProjectionInputCommandKind.DismissAssist:
                    dismissAssist.Add(chord);
                    break;
                case ProjectionInputCommandKind.PreviousContextItem:
                    prevContextItem.Add(chord);
                    break;
                case ProjectionInputCommandKind.NextContextItem:
                    nextContextItem.Add(chord);
                    break;
            }
        }

        private static KeyChord ToKeyChord(ProjectionKeyboardGesture gesture) {
            var modifiers = ConsoleModifiers.None;
            if (gesture.Shift) {
                modifiers |= ConsoleModifiers.Shift;
            }

            if (gesture.Alt) {
                modifiers |= ConsoleModifiers.Alt;
            }

            if (gesture.Control) {
                modifiers |= ConsoleModifiers.Control;
            }

            return new KeyChord {
                Key = gesture.Key switch {
                    "enter" => ConsoleKey.Enter,
                    "tab" => ConsoleKey.Tab,
                    "arrow-left" => ConsoleKey.LeftArrow,
                    "arrow-right" => ConsoleKey.RightArrow,
                    "arrow-up" => ConsoleKey.UpArrow,
                    "arrow-down" => ConsoleKey.DownArrow,
                    "escape" => ConsoleKey.Escape,
                    "space" => ConsoleKey.Spacebar,
                    [var c] when c is >= 'a' and <= 'z' => ConsoleKey.A + (c - 'a'),
                    [var c] when c is >= '0' and <= '9' => ConsoleKey.D0 + (c - '0'),
                    _ => throw new NotSupportedException($"Projection interaction compiler cannot map neutral key gesture '{gesture.Key}' to a console key."),
                },
                Modifiers = modifiers,
            };
        }

        private static EditorAuthority ToEditorAuthority(ProjectionInputAuthority authority) {
            return authority switch {
                ProjectionInputAuthority.HostBuffered => EditorAuthority.HostBuffered,
                ProjectionInputAuthority.Readonly => EditorAuthority.Readonly,
                _ => EditorAuthority.ClientBuffered,
            };
        }

        private static EditorPaneKind ToEditorPaneKind(ProjectionInputKind kind) {
            return kind switch {
                ProjectionInputKind.MultiLine => EditorPaneKind.MultiLine,
                ProjectionInputKind.ReadonlyBuffer => EditorPaneKind.ReadonlyBuffer,
                _ => EditorPaneKind.SingleLine,
            };
        }
    }
}
