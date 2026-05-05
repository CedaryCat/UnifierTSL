using UnifierTSL.Contracts.Projection;

namespace UnifierTSL.Contracts.Projection.BuiltIn {
    public static class SessionStatusProjectionSchema {
        public const string ScopeId = "session.status";
        public const string DocumentKind = "session.status";
        public const string RootNodeId = "status.root";
        public const string IndicatorNodeId = "status.indicator";
        public const string TitleNodeId = "status.title";
        public const string HeaderNodeId = "status.header";
        public const string CompactNodeId = "status.compact";
        public const string DetailNodeId = "status.detail";

        public static ProjectionDocumentDefinition Definition { get; } = new() {
            RootNodeIds = [RootNodeId],
            Nodes = [
                new ProjectionNodeDefinition {
                    NodeId = RootNodeId,
                    Kind = ProjectionNodeKind.Container,
                    ChildNodeIds = [IndicatorNodeId, TitleNodeId, HeaderNodeId, CompactNodeId, DetailNodeId],
                    Traits = new ProjectionNodeTraits {
                        HideWhenEmpty = true,
                    },
                },
                new ProjectionNodeDefinition {
                    NodeId = IndicatorNodeId,
                    Kind = ProjectionNodeKind.Text,
                    Role = ProjectionSemanticRole.Feedback,
                    Zone = ProjectionZone.Support,
                    Traits = new ProjectionNodeTraits {
                        HideWhenEmpty = true,
                    },
                },
                new ProjectionNodeDefinition {
                    NodeId = TitleNodeId,
                    Kind = ProjectionNodeKind.Text,
                    Role = ProjectionSemanticRole.Label,
                    SemanticKey = EditorProjectionSemanticKeys.StatusHeader,
                    Zone = ProjectionZone.Support,
                    Traits = new ProjectionNodeTraits {
                        HideWhenEmpty = true,
                    },
                },
                new ProjectionNodeDefinition {
                    NodeId = HeaderNodeId,
                    Kind = ProjectionNodeKind.Text,
                    Role = ProjectionSemanticRole.Metadata,
                    Zone = ProjectionZone.Support,
                    Traits = new ProjectionNodeTraits {
                        HideWhenEmpty = true,
                    },
                },
                new ProjectionNodeDefinition {
                    NodeId = CompactNodeId,
                    Kind = ProjectionNodeKind.Text,
                    Role = ProjectionSemanticRole.Summary,
                    SemanticKey = EditorProjectionSemanticKeys.StatusSummary,
                    Zone = ProjectionZone.Support,
                    Traits = new ProjectionNodeTraits {
                        HideWhenEmpty = true,
                    },
                },
                new ProjectionNodeDefinition {
                    NodeId = DetailNodeId,
                    Kind = ProjectionNodeKind.Detail,
                    Role = ProjectionSemanticRole.Detail,
                    SemanticKey = EditorProjectionSemanticKeys.StatusDetail,
                    Zone = ProjectionZone.Detail,
                    Traits = new ProjectionNodeTraits {
                        HideWhenEmpty = true,
                    },
                },
            ],
        };

        public static bool IsSessionStatusScope(ProjectionScope scope) {
            return scope.Kind == ProjectionScopeKind.Session
                && string.Equals(scope.DocumentKind, DocumentKind, StringComparison.Ordinal);
        }
    }
}
