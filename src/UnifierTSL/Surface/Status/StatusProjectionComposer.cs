using UnifierTSL.Surface.Activities;
using System.Globalization;
using UnifierTSL.Contracts.Display;
using UnifierTSL.Contracts.Projection;
using SessionStatusSchema = UnifierTSL.Contracts.Projection.BuiltIn.SessionStatusProjectionSchema;

namespace UnifierTSL.Surface.Status
{
    internal static class StatusProjectionComposer
    {
        private const int ActivitySpinnerFrameStepTicks = 7;
        private static readonly ProjectionNodeDefinition RootNodeDefinition = ResolveSchemaNode(SessionStatusSchema.RootNodeId);
        private static readonly ProjectionNodeDefinition IndicatorNodeDefinition = ResolveSchemaNode(SessionStatusSchema.IndicatorNodeId);
        private static readonly ProjectionNodeDefinition TitleNodeDefinition = ResolveSchemaNode(SessionStatusSchema.TitleNodeId);
        private static readonly ProjectionNodeDefinition CompactNodeDefinition = ResolveSchemaNode(SessionStatusSchema.CompactNodeId);
        private static readonly ProjectionNodeDefinition DetailNodeDefinition = ResolveSchemaNode(SessionStatusSchema.DetailNodeId);
        private static readonly StyledTextLine[] ActivitySpinnerIndicatorFrames = [
            BuildIndicatorFrame(@"[|]"),
            BuildIndicatorFrame(@"[/]"),
            BuildIndicatorFrame(@"[-]"),
            BuildIndicatorFrame(@"[\]"),
        ];

        public static ProjectionDocument Compose(
            ProjectionDocument? baselineDocument,
            ActivityViewSnapshot? activityViewSnapshot) {
            var hasBaseline = StatusProjectionDocumentFactory.HasVisibleContent(baselineDocument);
            var hasActivity = activityViewSnapshot.HasValue;
            if (!hasBaseline && !hasActivity) {
                return StatusProjectionDocumentFactory.CreateEmpty();
            }

            if (!hasActivity) {
                return baselineDocument ?? StatusProjectionDocumentFactory.CreateEmpty();
            }

            return ComposeWithActivity(
                hasBaseline ? baselineDocument : null,
                activityViewSnapshot!.Value);
        }

        private static ProjectionDocument ComposeWithActivity(
            ProjectionDocument? baselineDocument,
            ActivityViewSnapshot activityView) {
            List<ProjectionNodeDefinition> overlayDefinitions = [
                RootNodeDefinition,
                IndicatorNodeDefinition,
                CompactNodeDefinition,
                DetailNodeDefinition,
            ];
            List<ProjectionNodeState> overlayStates = [
                StatusProjectionDocumentFactory.CreateRootNode(isVisible: true),
                StatusProjectionDocumentFactory.CreateTextNode(
                    SessionStatusSchema.IndicatorNodeId,
                    animation: StatusProjectionDocumentFactory.CreateAnimation(
                        ActivitySpinnerFrameStepTicks,
                        ActivitySpinnerIndicatorFrames)),
                StatusProjectionDocumentFactory.CreateTextNode(
                    SessionStatusSchema.CompactNodeId,
                    StatusProjectionDocumentFactory.ToBlock(
                        ComposeCompactActivityLine(activityView),
                        SurfaceStyleCatalog.StatusBand)),
                StatusProjectionDocumentFactory.CreateDetailNode(
                    SessionStatusSchema.DetailNodeId,
                    ResolveDetailLines(baselineDocument, activityView),
                    ResolveDetailHeading(baselineDocument),
                    ResolveDetailSummary(baselineDocument)),
            ];
            if (baselineDocument is null) {
                overlayDefinitions.Add(TitleNodeDefinition);
                overlayStates.Add(StatusProjectionDocumentFactory.CreateTextNode(
                    SessionStatusSchema.TitleNodeId,
                    StatusProjectionDocumentFactory.CreateTitleBlock()));
            }

            return new ProjectionDocument {
                Scope = ResolveScope(baselineDocument),
                Definition = MergeDefinition(
                    baselineDocument?.Definition,
                    overlayDefinitions),
                State = new ProjectionDocumentState {
                    Focus = baselineDocument?.State.Focus ?? new ProjectionFocusState(),
                    Selection = baselineDocument?.State.Selection ?? new ProjectionSelectionSet(),
                    Styles = StatusProjectionDocumentFactory.MergeStyles(
                        StatusProjectionDocumentFactory.CreateProjectionStyleDictionary(),
                        baselineDocument?.State.Styles),
                    Nodes = MergeNodeStates(
                        baselineDocument?.State.Nodes,
                        overlayStates),
                },
            };
        }

        private static ProjectionTextBlock[] ResolveDetailLines(
            ProjectionDocument? baselineDocument,
            ActivityViewSnapshot activityView) {
            List<ProjectionTextBlock> lines = [
                StatusProjectionDocumentFactory.ToBlock(ComposeDetailLine(activityView)),
            ];
            if (activityView.ActivityCount > 1) {
                lines.Add(StatusProjectionDocumentFactory.ToBlock(ComposeTaskListLine(activityView)));
            }

            if (StatusProjectionDocumentFactory.FindDetailNode(baselineDocument, SessionStatusSchema.DetailNodeId)?.State.Lines
                is { Length: > 0 } baselineLines) {
                lines.AddRange(baselineLines.Where(StatusProjectionDocumentFactory.HasVisibleText));
            }

            return [.. lines];
        }

        private static ProjectionTextBlock ResolveDetailHeading(ProjectionDocument? baselineDocument) {
            return StatusProjectionDocumentFactory.FindDetailNode(baselineDocument, SessionStatusSchema.DetailNodeId)?.State.Heading
                ?? new ProjectionTextBlock();
        }

        private static ProjectionTextBlock ResolveDetailSummary(ProjectionDocument? baselineDocument) {
            return StatusProjectionDocumentFactory.FindDetailNode(baselineDocument, SessionStatusSchema.DetailNodeId)?.State.Summary
                ?? new ProjectionTextBlock();
        }

        private static StyledTextLine ComposeCompactActivityLine(ActivityViewSnapshot activityView) {
            var activity = activityView.SelectedActivity;
            var progressText = ComposeProgressText(activity, wrapInBrackets: false);
            var indexText = activityView.ActivityCount > 1
                ? $"{activityView.SelectedIndex + 1}/{activityView.ActivityCount} "
                : string.Empty;
            StyledTextLineBuilder line = new();
            line.Append("[");
            if (!string.IsNullOrEmpty(indexText)) {
                line.Append(indexText);
            }

            line.Append(activity.Category, SurfaceStyleCatalog.Accent);
            if (!string.IsNullOrEmpty(progressText)) {
                line.Append(":");
                line.Append(progressText);
            }

            line.Append("]");
            return line.Build();
        }

        private static StyledTextLine ComposeDetailLine(ActivityViewSnapshot activityView) {
            var activity = activityView.SelectedActivity;
            StyledTextLineBuilder line = new();
            line.Append(activityView.ActivityCount > 1
                ? $"Task[{activityView.SelectedIndex + 1}/{activityView.ActivityCount}] "
                : "Task ");
            line.Append(activity.Category, SurfaceStyleCatalog.Accent);
            line.Append($": {activity.Message}");

            var progressText = ComposeProgressText(activity, wrapInBrackets: true);
            if (!string.IsNullOrEmpty(progressText)) {
                line.Append($" {progressText}");
            }

            if (!activity.HideElapsed) {
                line.Append($" {FormatElapsed(activity.Elapsed)}");
            }

            if (activity.IsCancellationRequested) {
                line.Append(" canceling", SurfaceStyleCatalog.Warning);
            }

            return line.Build();
        }

        private static StyledTextLine ComposeTaskListLine(ActivityViewSnapshot activityView) {
            StyledTextLineBuilder line = new();
            line.Append("Tasks:");
            for (var i = 0; i < activityView.ActivityCategories.Length; i++) {
                line.Append(" | ");
                line.Append(
                    i == activityView.SelectedIndex
                        ? $">{i + 1} {activityView.ActivityCategories[i]}"
                        : $"{i + 1} {activityView.ActivityCategories[i]}",
                    i == activityView.SelectedIndex ? SurfaceStyleCatalog.Accent : string.Empty);
            }

            return line.Build();
        }

        private static string ComposeProgressText(ActivityStatusSnapshot activity, bool wrapInBrackets) {
            if (!activity.ProgressEnabled) {
                return string.Empty;
            }

            string payload;
            if (activity.ProgressTotal > 0) {
                payload = activity.ProgressStyle switch {
                    ActivityProgressStyle.Percent => string.Create(CultureInfo.InvariantCulture, $"{Math.Clamp(
                        (double)activity.ProgressCurrent / activity.ProgressTotal,
                        0d,
                        1d) * 100d:0.0}%"),
                    _ => $"{activity.ProgressCurrent}/{activity.ProgressTotal}",
                };
            }
            else {
                payload = activity.ProgressCurrent.ToString(CultureInfo.InvariantCulture);
            }

            return wrapInBrackets ? $"[{payload}]" : payload;
        }

        private static string FormatElapsed(TimeSpan elapsed) {
            if (elapsed < TimeSpan.FromSeconds(1)) {
                return $"{Math.Max(0, elapsed.TotalMilliseconds):0}ms";
            }

            if (elapsed < TimeSpan.FromMinutes(1)) {
                return $"{elapsed.TotalSeconds:0.0}s";
            }

            if (elapsed < TimeSpan.FromHours(1)) {
                return $"{(int)elapsed.TotalMinutes}m{elapsed.Seconds:00}s";
            }

            return $"{(int)elapsed.TotalHours}h{elapsed.Minutes:00}m";
        }

        private static StyledTextLine BuildIndicatorFrame(string indicatorText) {
            return new StyledTextLine {
                Runs = string.IsNullOrEmpty(indicatorText)
                    ? []
                    : [
                        new StyledTextRun {
                            Text = indicatorText,
                            StyleId = SurfaceStyleCatalog.StatusHeaderIndicator,
                        },
                    ],
            };
        }

        private static ProjectionScope ResolveScope(ProjectionDocument? baselineDocument) {
            return baselineDocument is not null && SessionStatusSchema.IsSessionStatusScope(baselineDocument.Scope)
                ? baselineDocument.Scope
                : new ProjectionScope {
                    Kind = ProjectionScopeKind.Session,
                    ScopeId = SessionStatusSchema.ScopeId,
                    DocumentKind = SessionStatusSchema.DocumentKind,
                };
        }

        private static ProjectionDocumentDefinition MergeDefinition(
            ProjectionDocumentDefinition? baselineDefinition,
            IReadOnlyList<ProjectionNodeDefinition> overlayDefinitions) {
            var rootIds = new List<string>(baselineDefinition?.RootNodeIds ?? []);
            var rootIdSet = new HashSet<string>(rootIds, StringComparer.Ordinal);
            var nodes = new List<ProjectionNodeDefinition>(baselineDefinition?.Nodes ?? []);
            var nodesById = nodes.ToDictionary(static node => node.NodeId, StringComparer.Ordinal);

            foreach (var overlayDefinition in overlayDefinitions) {
                if (nodesById.TryGetValue(overlayDefinition.NodeId, out var existing)) {
                    if (string.Equals(existing.NodeId, SessionStatusSchema.RootNodeId, StringComparison.Ordinal)) {
                        var mergedChildren = existing.ChildNodeIds
                            .Concat(overlayDefinition.ChildNodeIds ?? [])
                            .Where(static childId => !string.IsNullOrWhiteSpace(childId))
                            .Distinct(StringComparer.Ordinal)
                            .ToArray();
                        var mergedRoot = new ProjectionNodeDefinition {
                            NodeId = existing.NodeId,
                            Kind = existing.Kind,
                            Role = existing.Role,
                            SemanticKey = existing.SemanticKey,
                            Zone = existing.Zone,
                            ChildNodeIds = mergedChildren,
                            Traits = existing.Traits,
                            Bindings = existing.Bindings,
                        };
                        var index = nodes.FindIndex(node => string.Equals(node.NodeId, existing.NodeId, StringComparison.Ordinal));
                        nodes[index] = mergedRoot;
                        nodesById[mergedRoot.NodeId] = mergedRoot;
                    }
                }
                else {
                    nodes.Add(overlayDefinition);
                    nodesById[overlayDefinition.NodeId] = overlayDefinition;
                }

                if (string.Equals(overlayDefinition.NodeId, SessionStatusSchema.RootNodeId, StringComparison.Ordinal)
                    && rootIdSet.Add(overlayDefinition.NodeId)) {
                    rootIds.Add(overlayDefinition.NodeId);
                }
            }

            return new ProjectionDocumentDefinition {
                RootNodeIds = [.. rootIds],
                Nodes = [.. nodes],
                Traits = baselineDefinition?.Traits ?? SessionStatusSchema.Definition.Traits,
            };
        }

        private static ProjectionNodeState[] MergeNodeStates(
            IReadOnlyList<ProjectionNodeState>? baselineStates,
            IReadOnlyList<ProjectionNodeState> overlayStates) {
            List<string> orderedNodeIds = [];
            HashSet<string> seen = new(StringComparer.Ordinal);
            Dictionary<string, ProjectionNodeState> statesById = new(StringComparer.Ordinal);

            foreach (var state in baselineStates ?? []) {
                if (state is null || string.IsNullOrWhiteSpace(state.NodeId)) {
                    continue;
                }

                if (seen.Add(state.NodeId)) {
                    orderedNodeIds.Add(state.NodeId);
                }

                statesById[state.NodeId] = state;
            }

            foreach (var state in overlayStates) {
                if (state is null || string.IsNullOrWhiteSpace(state.NodeId)) {
                    continue;
                }

                if (seen.Add(state.NodeId)) {
                    orderedNodeIds.Add(state.NodeId);
                }

                statesById[state.NodeId] = state;
            }

            return [.. orderedNodeIds.Select(nodeId => statesById[nodeId])];
        }

        private static ProjectionNodeDefinition ResolveSchemaNode(string nodeId) {
            return SessionStatusSchema.Definition.Nodes.First(node => string.Equals(node.NodeId, nodeId, StringComparison.Ordinal));
        }
    }
}
