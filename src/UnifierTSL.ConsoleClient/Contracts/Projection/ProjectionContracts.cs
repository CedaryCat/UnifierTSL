using MemoryPack;

namespace UnifierTSL.Contracts.Projection {
    public enum ProjectionScopeKind : byte {
        Session,
        Interaction,
    }

    public enum ProjectionNodeKind : byte {
        Container,
        Text,
        EditableText,
        Collection,
        PropertySet,
        Detail,
    }

    public enum ProjectionSemanticRole : byte {
        None,
        Input,
        Label,
        Preview,
        Options,
        Context,
        Detail,
        Summary,
        Metadata,
        Feedback,
    }

    public enum ProjectionZone : byte {
        Primary,
        Support,
        Detail,
        Context,
        Auxiliary,
        Modal,
        Background,
    }

    public enum ProjectionDensityHint : byte {
        Default,
        Compact,
        Relaxed,
    }

    public enum ProjectionExpansionHint : byte {
        Default,
        Collapsed,
        Expanded,
    }

    public enum ProjectionCollectionPresentationHint : byte {
        Default,
        List,
        Inline,
        Grid,
    }

    public enum ProjectionNodeBindingKind : byte {
        Context,
        ActiveItemSource,
        LabelFor,
        DecorationOf,
    }

    [MemoryPackable]
    public sealed partial class ProjectionDocument {
        public ProjectionScope Scope { get; init; } = new();
        public ProjectionDocumentDefinition Definition { get; init; } = new();
        public ProjectionDocumentState State { get; init; } = new();
    }

    [MemoryPackable]
    public sealed partial class ProjectionDocumentContent {
        public ProjectionDocumentDefinition Definition { get; init; } = new();
        public ProjectionDocumentState State { get; init; } = new();
    }

    [MemoryPackable]
    public sealed partial class ProjectionScope {
        public ProjectionScopeKind Kind { get; init; }
        public string ScopeId { get; init; } = string.Empty;
        public string DocumentKind { get; init; } = string.Empty;
    }

    [MemoryPackable]
    public sealed partial class ProjectionDocumentDefinition {
        public string[] RootNodeIds { get; init; } = [];
        public ProjectionNodeDefinition[] Nodes { get; init; } = [];
        public ProjectionMarkerCatalogItem[] MarkerCatalog { get; init; } = [];
        public ProjectionDocumentTraits Traits { get; init; } = new();
    }

    [MemoryPackable]
    public sealed partial class ProjectionDocumentTraits {
        public bool SupportsFocus { get; init; } = true;
        public bool SupportsSelection { get; init; } = true;
        // Incremental transport is explicit opt-in; mainline projection flow still publishes full snapshots.
        public bool SupportsIncrementalUpdates { get; init; }
    }

    [MemoryPackable]
    public sealed partial class ProjectionDocumentState {
        public ProjectionNodeState[] Nodes { get; init; } = [];
        public ProjectionFocusState Focus { get; init; } = new();
        public ProjectionSelectionSet Selection { get; init; } = new();
        public ProjectionStyleDictionary Styles { get; init; } = new();
    }

    [MemoryPackable]
    public sealed partial class ProjectionDocumentPatch {
        public ProjectionNodePatch[] Nodes { get; init; } = [];
        public ProjectionFocusState? Focus { get; init; }
        public ProjectionSelectionSet? Selection { get; init; }
        public ProjectionStyleDictionary? Styles { get; init; }
    }

    [MemoryPackable]
    public sealed partial class ProjectionNodeDefinition {
        public string NodeId { get; init; } = string.Empty;
        public ProjectionNodeKind Kind { get; init; }
        public ProjectionSemanticRole Role { get; init; }
        public string SemanticKey { get; init; } = string.Empty;
        public ProjectionZone Zone { get; init; } = ProjectionZone.Primary;
        public string[] ChildNodeIds { get; init; } = [];
        public ProjectionNodeTraits Traits { get; init; } = new();
        public ProjectionNodeBindingSet Bindings { get; init; } = new();
    }

    [MemoryPackable]
    public sealed partial class ProjectionNodeTraits {
        public bool IsFocusable { get; init; }
        public bool IsInteractive { get; init; }
        public bool IsTransient { get; init; }
        public bool HideWhenEmpty { get; init; }
        public ProjectionInputPolicy Input { get; init; } = new();
        public ProjectionDensityHint DensityHint { get; init; }
        public ProjectionExpansionHint ExpansionHint { get; init; }
        public ProjectionCollectionPresentationHint CollectionHint { get; init; }
    }

    [MemoryPackable]
    public sealed partial class ProjectionNodeBindingSet {
        public ProjectionNodeBinding[] Links { get; init; } = [];
    }

    [MemoryPackable]
    public sealed partial class ProjectionNodeBinding {
        public ProjectionNodeBindingKind Kind { get; init; }
        public string TargetNodeId { get; init; } = string.Empty;
    }

    [MemoryPackable]
    [MemoryPackUnion(0, typeof(ContainerProjectionNodeState))]
    [MemoryPackUnion(1, typeof(TextProjectionNodeState))]
    [MemoryPackUnion(2, typeof(EditableTextProjectionNodeState))]
    [MemoryPackUnion(3, typeof(CollectionProjectionNodeState))]
    [MemoryPackUnion(4, typeof(PropertySetProjectionNodeState))]
    [MemoryPackUnion(5, typeof(DetailProjectionNodeState))]
    public abstract partial class ProjectionNodeState {
        public string NodeId { get; init; } = string.Empty;
        public abstract ProjectionNodeKind Kind { get; }
    }

    [MemoryPackable]
    public sealed partial class ContainerProjectionNodeState : ProjectionNodeState {
        public override ProjectionNodeKind Kind => ProjectionNodeKind.Container;
        public ContainerNodeState State { get; init; } = new();
    }

    [MemoryPackable]
    public sealed partial class TextProjectionNodeState : ProjectionNodeState {
        public override ProjectionNodeKind Kind => ProjectionNodeKind.Text;
        public TextNodeState State { get; init; } = new();
    }

    [MemoryPackable]
    public sealed partial class EditableTextProjectionNodeState : ProjectionNodeState {
        public override ProjectionNodeKind Kind => ProjectionNodeKind.EditableText;
        public EditableTextNodeState State { get; init; } = new();
    }

    [MemoryPackable]
    public sealed partial class CollectionProjectionNodeState : ProjectionNodeState {
        public override ProjectionNodeKind Kind => ProjectionNodeKind.Collection;
        public CollectionNodeState State { get; init; } = new();
    }

    [MemoryPackable]
    public sealed partial class PropertySetProjectionNodeState : ProjectionNodeState {
        public override ProjectionNodeKind Kind => ProjectionNodeKind.PropertySet;
        public PropertySetNodeState State { get; init; } = new();
    }

    [MemoryPackable]
    public sealed partial class DetailProjectionNodeState : ProjectionNodeState {
        public override ProjectionNodeKind Kind => ProjectionNodeKind.Detail;
        public DetailNodeState State { get; init; } = new();
    }

    [MemoryPackable]
    public sealed partial class ProjectionNodePatch {
        public required ProjectionNodeState State { get; init; }
    }

    [MemoryPackable]
    public sealed partial class ProjectionFocusState {
        public string NodeId { get; init; } = string.Empty;
        public string ItemId { get; init; } = string.Empty;
        public bool IsPrimary { get; init; } = true;
    }

    [MemoryPackable]
    public sealed partial class ProjectionSelectionSet {
        public ProjectionNodeSelectionState[] Nodes { get; init; } = [];
    }

    [MemoryPackable]
    public sealed partial class ProjectionNodeSelectionState {
        public string NodeId { get; init; } = string.Empty;
        public string ActiveItemId { get; init; } = string.Empty;
        public string[] SelectedItemIds { get; init; } = [];
    }

    [MemoryPackable]
    public sealed partial class ContainerNodeState {
        public bool IsVisible { get; init; } = true;
    }

    [MemoryPackable]
    public sealed partial class TextNodeState {
        public ProjectionTextBlock Content { get; init; } = new();
        public ProjectionTextAnimation? Animation { get; init; }
    }

    public enum ProjectionInlineDecorationKind : byte {
        GhostText,
        Highlight,
        Annotation,
    }

    public enum ProjectionEmptySubmitAction : byte {
        KeepBuffer,
        AcceptPreviewIfAvailable,
    }

    [MemoryPackable]
    public sealed partial class ProjectionInlineDecoration {
        public ProjectionInlineDecorationKind Kind { get; init; }
        public int StartIndex { get; init; }
        public int Length { get; init; }
        public ProjectionStyleDefinition? Style { get; init; }
        public ProjectionTextBlock Content { get; init; } = new();
    }

    [MemoryPackable]
    public sealed partial class ProjectionTextMarker {
        public string Key { get; init; } = string.Empty;
        public string VariantKey { get; init; } = string.Empty;
        public int StartIndex { get; init; }
        public int Length { get; init; }
    }

    [MemoryPackable]
    public sealed partial class ProjectionMarkerCatalogItem {
        public string Key { get; init; } = string.Empty;
        public string VariantKey { get; init; } = string.Empty;
        public string DisplayText { get; init; } = string.Empty;
        public ProjectionStyleDefinition? Style { get; init; }
    }

    [MemoryPackable]
    public sealed partial class ProjectionSubmitState {
        public bool AcceptsSubmit { get; init; } = true;
        public bool IsReady { get; init; } = true;
        public string Reason { get; init; } = string.Empty;
        public ProjectionEmptySubmitAction EmptyInputAction { get; init; }
        public bool AlternateSubmitBypassesPreview { get; init; } = true;
    }

    [MemoryPackable]
    public sealed partial class EditableTextNodeState {
        public string BufferText { get; init; } = string.Empty;
        public int CaretIndex { get; init; }
        public long ExpectedClientBufferRevision { get; init; }
        public long RemoteRevision { get; init; }
        public ProjectionTextMarker[] Markers { get; init; } = [];
        public ProjectionInlineDecoration[] Decorations { get; init; } = [];
        public ProjectionSubmitState Submit { get; init; } = new();
    }

    [MemoryPackable]
    public sealed partial class ProjectionTextEditOperation {
        public string TargetNodeId { get; init; } = string.Empty;
        public int StartIndex { get; init; }
        public int Length { get; init; }
        public string NewText { get; init; } = string.Empty;
    }

    [MemoryPackable]
    public sealed partial class CollectionNodeState {
        public ProjectionCollectionItem[] Items { get; init; } = [];
        public int TotalItemCount { get; init; }
        public int WindowOffset { get; init; }
        public int PageSize { get; init; }
        public bool IsPaged { get; init; }
    }

    [MemoryPackable]
    public sealed partial class ProjectionCollectionItem {
        public string ItemId { get; init; } = string.Empty;
        public ProjectionTextBlock Label { get; init; } = new();
        public ProjectionTextBlock SecondaryLabel { get; init; } = new();
        public ProjectionTextBlock TrailingLabel { get; init; } = new();
        public ProjectionTextBlock Summary { get; init; } = new();
        public ProjectionTextBlock Detail { get; init; } = new();
        public ProjectionTextEditOperation PrimaryEdit { get; init; } = new();
        public bool IsEnabled { get; init; } = true;
    }

    [MemoryPackable]
    public sealed partial class PropertySetNodeState {
        public ProjectionPropertyValue[] Properties { get; init; } = [];
    }

    [MemoryPackable]
    public sealed partial class ProjectionPropertyValue {
        public string PropertyKey { get; init; } = string.Empty;
        public ProjectionTextBlock Label { get; init; } = new();
        public ProjectionTextBlock Value { get; init; } = new();
        public ProjectionStyleDefinition? Style { get; init; }
        public bool IsVisible { get; init; } = true;
    }

    [MemoryPackable]
    public sealed partial class DetailNodeState {
        public string ContextItemId { get; init; } = string.Empty;
        public ProjectionTextBlock Heading { get; init; } = new();
        public ProjectionTextBlock Summary { get; init; } = new();
        public ProjectionTextBlock[] Lines { get; init; } = [];
    }
}
