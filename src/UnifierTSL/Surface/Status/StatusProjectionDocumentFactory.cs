using UnifierTSL.Contracts.Display;
using UnifierTSL.Contracts.Projection;
using UnifierTSL.Surface;
using SessionStatusSchema = UnifierTSL.Contracts.Projection.BuiltIn.SessionStatusProjectionSchema;

namespace UnifierTSL.Surface.Status {
    internal static class StatusProjectionDocumentFactory {
        public const string TitleText = "STATUS";

        // Status producers should author session-status projection content directly.
        // This helper is only the built-in convenience wrapper used by launcher/server
        // status composition; it must not be treated as the only valid status shape.
        public static ProjectionDocument Create(ProjectionStyleDictionary? styles = null, params ProjectionNodeState[] nodes) {
            return new ProjectionDocument {
                Scope = new ProjectionScope {
                    Kind = ProjectionScopeKind.Session,
                    ScopeId = SessionStatusSchema.ScopeId,
                    DocumentKind = SessionStatusSchema.DocumentKind,
                },
                Definition = SessionStatusSchema.Definition,
                State = new ProjectionDocumentState {
                    Styles = styles ?? CreateProjectionStyleDictionary(),
                    Nodes = EnsureRootNode(nodes),
                },
            };
        }

        public static ProjectionDocument CreateEmpty(ProjectionStyleDictionary? styles = null) {
            return Create(
                styles,
                CreateRootNode(isVisible: false));
        }

        public static ProjectionStyleDictionary CreateProjectionStyleDictionary(StyleDictionary? source = null) {
            var resolvedSource = StyleDictionaryOps.Merge(SurfaceStyleCatalog.Default, source);
            Dictionary<string, ProjectionStyleDefinition> stylesById = new(StringComparer.Ordinal);
            foreach (var style in resolvedSource.Styles ?? []) {
                if (style is null || string.IsNullOrWhiteSpace(style.StyleId)) {
                    continue;
                }

                stylesById[style.StyleId] = new ProjectionStyleDefinition {
                    Key = style.StyleId,
                    Foreground = CloneColor(style.Foreground),
                    Background = CloneColor(style.Background),
                    TextAttributes = CloneTextAttributes(style.TextAttributes),
                };
            }

            stylesById[SurfaceStyleCatalog.StatusBand] = CreateStatusBandProjectionStyle();
            stylesById[SurfaceStyleCatalog.StatusTitle] = CreateStatusTitleProjectionStyle();
            stylesById[SurfaceStyleCatalog.StatusSummary] = CreateStatusSummaryProjectionStyle();

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

            stylesById[SurfaceStyleCatalog.StatusBand] = CreateStatusBandProjectionStyle();
            stylesById[SurfaceStyleCatalog.StatusTitle] = CreateStatusTitleProjectionStyle();
            stylesById[SurfaceStyleCatalog.StatusSummary] = CreateStatusSummaryProjectionStyle();
            return ProjectionStyleDictionaryOps.WithSlots([.. stylesById.Values]);
        }

        public static ContainerProjectionNodeState CreateRootNode(bool isVisible = true) {
            return new ContainerProjectionNodeState {
                NodeId = SessionStatusSchema.RootNodeId,
                State = new ContainerNodeState {
                    IsVisible = isVisible,
                },
            };
        }

        public static TextProjectionNodeState CreateTextNode(
            string nodeId,
            ProjectionTextBlock? content = null,
            ProjectionTextAnimation? animation = null) {
            return new TextProjectionNodeState {
                NodeId = nodeId,
                State = new TextNodeState {
                    Content = content ?? new ProjectionTextBlock(),
                    Animation = animation,
                },
            };
        }

        public static DetailProjectionNodeState CreateDetailNode(
            string nodeId,
            IReadOnlyList<ProjectionTextBlock>? lines = null,
            ProjectionTextBlock? heading = null,
            ProjectionTextBlock? summary = null) {
            return new DetailProjectionNodeState {
                NodeId = nodeId,
                State = new DetailNodeState {
                    Heading = heading ?? new ProjectionTextBlock(),
                    Summary = summary ?? new ProjectionTextBlock(),
                    Lines = lines is not { Count: > 0 } ? [] : [.. lines],
                },
            };
        }

        public static ProjectionTextBlock CreateSingleLineBlock(string? text, string spanStyleKey = "", string lineStyleKey = "") {
            return string.IsNullOrEmpty(text)
                ? new ProjectionTextBlock()
                : new ProjectionTextBlock {
                    Lines = [
                        new ProjectionTextLine {
                            Style = ProjectionStyleDictionaryOps.Reference(lineStyleKey),
                            Spans = [
                                new ProjectionTextSpan {
                                    Text = text,
                                    Style = ProjectionStyleDictionaryOps.Reference(spanStyleKey),
                                },
                            ],
                        },
                    ],
                };
        }

        public static ProjectionTextBlock CreateTitleBlock() {
            return CreateSingleLineBlock(TitleText, lineStyleKey: SurfaceStyleCatalog.StatusBand);
        }

        public static ProjectionTextBlock ToBlock(StyledTextLine? line, string? fallbackLineStyleKey = null) {
            return line is null || !StyledTextLineOps.HasVisibleText(line)
                ? new ProjectionTextBlock()
                : new ProjectionTextBlock {
                    Lines = [
                        new ProjectionTextLine {
                            Style = ProjectionStyleDictionaryOps.Reference(!string.IsNullOrWhiteSpace(line.LineStyleId)
                                ? line.LineStyleId
                                : fallbackLineStyleKey ?? string.Empty),
                            Spans = [.. (line.Runs ?? [])
                                .Where(static run => !string.IsNullOrEmpty(run.Text))
                                .Select(static run => new ProjectionTextSpan {
                                    Text = run.Text ?? string.Empty,
                                    Style = ProjectionStyleDictionaryOps.Reference(run.StyleId),
                                })],
                        },
                    ],
                };
        }

        public static ProjectionTextBlock[] ToBlocks(IReadOnlyList<StyledTextLine>? lines, string? fallbackLineStyleKey = null) {
            return lines is not { Count: > 0 }
                ? []
                : [.. lines.Select(line => ToBlock(line, fallbackLineStyleKey))];
        }

        public static ProjectionTextAnimation? CreateAnimation(
            int frameStepTicks,
            IReadOnlyList<StyledTextLine>? frames,
            string? fallbackLineStyleKey = null) {
            if (frames is not { Count: > 0 }) {
                return null;
            }

            return new ProjectionTextAnimation {
                FrameStepTicks = frames.Count <= 1
                    ? 0
                    : Math.Max(0, frameStepTicks),
                Frames = [.. frames.Select(line => ToBlock(line, fallbackLineStyleKey))],
            };
        }

        public static bool HasVisibleContent(ProjectionDocument? document) {
            if (document is null) {
                return false;
            }

            if (AreAllRootsHidden(document)) {
                return false;
            }

            return (document.State.Nodes ?? []).Any(HasVisibleNodeContent);
        }

        internal static StyledTextStyle CreateStatusBandTextStyle(string styleId = SurfaceStyleCatalog.StatusBand) {
            return SurfaceRuntimeOptions.UseColorfulStatus
                ? new StyledTextStyle {
                    StyleId = styleId,
                    Foreground = Rgb(255, 255, 255),
                    Background = Rgb(0, 128, 128),
                }
                : new StyledTextStyle {
                    StyleId = styleId,
                    Foreground = Rgb(0, 0, 0),
                    Background = Rgb(0, 255, 255),
                };
        }

        internal static StyledTextStyle CreateStatusTitleTextStyle() {
            return CreateStatusBandTextStyle(SurfaceStyleCatalog.StatusTitle);
        }

        internal static StyledTextStyle CreateStatusSummaryTextStyle(string styleId = SurfaceStyleCatalog.StatusSummary) {
            return SurfaceRuntimeOptions.UseColorfulStatus
                ? new StyledTextStyle {
                    StyleId = styleId,
                    Foreground = Rgb(255, 255, 255),
                }
                : new StyledTextStyle {
                    StyleId = styleId,
                    Foreground = Rgb(0, 0, 0),
                };
        }

        private static ProjectionStyleDefinition CreateStatusBandProjectionStyle() {
            return CreateProjectionStyle(CreateStatusBandTextStyle());
        }

        private static ProjectionStyleDefinition CreateStatusTitleProjectionStyle() {
            return CreateProjectionStyle(CreateStatusTitleTextStyle());
        }

        private static ProjectionStyleDefinition CreateStatusSummaryProjectionStyle() {
            return CreateProjectionStyle(CreateStatusSummaryTextStyle());
        }

        private static ProjectionStyleDefinition CreateProjectionStyle(StyledTextStyle style) {
            return new ProjectionStyleDefinition {
                Key = style.StyleId,
                Foreground = CloneColor(style.Foreground),
                Background = CloneColor(style.Background),
                TextAttributes = CloneTextAttributes(style.TextAttributes),
            };
        }

        public static TextProjectionNodeState? FindTextNode(ProjectionDocument? document, string nodeId) {
            return document?.State.Nodes?
                .OfType<TextProjectionNodeState>()
                .FirstOrDefault(node => string.Equals(node.NodeId, nodeId, StringComparison.Ordinal));
        }

        public static DetailProjectionNodeState? FindDetailNode(ProjectionDocument? document, string nodeId) {
            return document?.State.Nodes?
                .OfType<DetailProjectionNodeState>()
                .FirstOrDefault(node => string.Equals(node.NodeId, nodeId, StringComparison.Ordinal));
        }

        public static ContainerProjectionNodeState? FindContainerNode(ProjectionDocument? document, string nodeId) {
            return document?.State.Nodes?
                .OfType<ContainerProjectionNodeState>()
                .FirstOrDefault(node => string.Equals(node.NodeId, nodeId, StringComparison.Ordinal));
        }

        public static bool HasVisibleText(ProjectionTextBlock? block) {
            return block?.Lines is { Length: > 0 } lines
                && lines.Any(line => line.Spans?.Any(static span => !string.IsNullOrWhiteSpace(span.Text)) ?? false);
        }

        public static bool HasVisibleText(IReadOnlyList<ProjectionTextBlock>? blocks) {
            return blocks is { Count: > 0 } && blocks.Any(HasVisibleText);
        }

        private static bool HasVisibleAnimation(ProjectionTextAnimation? animation) {
            return animation?.Frames is { Length: > 0 } frames && frames.Any(HasVisibleText);
        }

        private static bool AreAllRootsHidden(ProjectionDocument document) {
            var rootNodeIds = document.Definition.RootNodeIds ?? [];
            if (rootNodeIds.Length == 0) {
                return false;
            }

            var hasResolvedRoot = false;
            foreach (var rootNodeId in rootNodeIds) {
                var root = FindContainerNode(document, rootNodeId);
                if (root is null || root.State.IsVisible) {
                    return false;
                }

                hasResolvedRoot = true;
            }

            return hasResolvedRoot;
        }

        private static bool HasVisibleNodeContent(ProjectionNodeState node) {
            return node switch {
                TextProjectionNodeState text => HasVisibleText(text.State.Content) || HasVisibleAnimation(text.State.Animation),
                EditableTextProjectionNodeState editable => !string.IsNullOrWhiteSpace(editable.State.BufferText)
                    || (editable.State.Decorations ?? []).Any(static decoration => HasVisibleText(decoration.Content)),
                CollectionProjectionNodeState collection => (collection.State.Items?.Length ?? 0) > 0 || collection.State.TotalItemCount > 0,
                PropertySetProjectionNodeState propertySet => (propertySet.State.Properties ?? [])
                    .Where(static property => property.IsVisible)
                    .Any(property => HasVisibleText(property.Label) || HasVisibleText(property.Value)),
                DetailProjectionNodeState detail => HasVisibleText(detail.State.Heading)
                    || HasVisibleText(detail.State.Summary)
                    || HasVisibleText(detail.State.Lines),
                _ => false,
            };
        }

        private static ProjectionNodeState[] EnsureRootNode(IReadOnlyList<ProjectionNodeState>? nodes) {
            List<ProjectionNodeState> normalized = [];
            var hasRoot = false;
            foreach (var node in nodes ?? []) {
                if (node is null || string.IsNullOrWhiteSpace(node.NodeId)) {
                    continue;
                }

                if (string.Equals(node.NodeId, SessionStatusSchema.RootNodeId, StringComparison.Ordinal)) {
                    hasRoot = true;
                }

                normalized.Add(node);
            }

            if (!hasRoot) {
                normalized.Insert(0, CreateRootNode());
            }

            return [.. normalized];
        }

        private static StyledColorValue Rgb(byte red, byte green, byte blue) {
            return new StyledColorValue {
                Red = red,
                Green = green,
                Blue = blue,
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
