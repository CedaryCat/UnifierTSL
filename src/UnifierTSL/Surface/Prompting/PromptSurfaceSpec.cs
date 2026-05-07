using System.Collections.Immutable;
using UnifierTSL.Surface.Prompting.Semantics;
using UnifierTSL.Surface.Prompting;
using UnifierTSL.Commanding;
using UnifierTSL.Contracts.Display;
using UnifierTSL.Contracts.Projection;
using UnifierTSL.Surface.Prompting.Model;
using UnifierTSL.Surface.Status;
using UnifierTSL.Contracts.Terminal;
using UnifierTSL.Servers;

namespace UnifierTSL.Surface.Prompting
{
    public enum PromptSurfaceScenario : byte
    {
        LocalInteractive,
        PagedInitial,
        PagedReactive,
    }

    public static class PromptSuggestionCatalog
    {
        public static ImmutableArray<PromptSuggestion> DefaultBooleanSuggestions { get; } = [
            new PromptSuggestion("true"),
            new PromptSuggestion("false"),
            new PromptSuggestion("on"),
            new PromptSuggestion("off"),
            new PromptSuggestion("1"),
            new PromptSuggestion("0"),
        ];
    }

    public readonly record struct PromptSuggestion(string Value, int Weight = 0);

    internal readonly record struct PromptBufferedAuthoringOptions
    {
        public PromptBufferedAuthoringOptions() {
        }

        public EditorPaneKind EditorKind { get; init; } = EditorPaneKind.SingleLine;
        public EditorKeymap Keymap { get; init; } = PromptEditorKeymaps.CreateSingleLine();
        public EditorAuthoringBehavior AuthoringBehavior { get; init; } = new() {
            OpensCompletionAutomatically = false,
        };
        public PromptSubmitReadiness PlainEnterReadiness { get; init; } = PromptSubmitReadiness.UseFallback;
        public bool AnalyzeCurrentLogicalLine { get; init; }
    }

    public readonly record struct PromptResolveContext(
        PromptInputPurpose Purpose,
        PromptInputState State,
        PromptSurfaceScenario Scenario);

    public interface IPromptRuntimeResolver
    {
        long GetRevision(PromptResolveContext context);

        ProjectionDocumentContent Resolve(PromptResolveContext context);
    }

    public static class PromptSurfaceRuntimeResolver
    {
        public static IPromptRuntimeResolver Create(
            Func<PromptResolveContext, ProjectionDocumentContent> resolve,
            Func<PromptResolveContext, long> getRevision) {

            ArgumentNullException.ThrowIfNull(resolve);
            ArgumentNullException.ThrowIfNull(getRevision);
            return new DelegatePromptResolver(resolve, getRevision);
        }

        private sealed class DelegatePromptResolver(
            Func<PromptResolveContext, ProjectionDocumentContent> resolve,
            Func<PromptResolveContext, long> getRevision) : IPromptRuntimeResolver
        {

            public long GetRevision(PromptResolveContext context) {
                return getRevision(context);
            }

            public ProjectionDocumentContent Resolve(PromptResolveContext context) {
                return resolve(context);
            }
        }
    }

    public sealed class PromptSurfaceSpec
    {
        public PromptInputPurpose Purpose { get; init; } = PromptInputPurpose.Plain;

        public ServerContext? Server { get; init; }

        public ProjectionDocumentContent Content { get; init; } = CreateContent("> ");

        public PromptSemanticSpec? SemanticSpec { get; init; }

        public ImmutableDictionary<string, ImmutableArray<PromptSuggestion>> StaticCandidates { get; init; } =
            ImmutableDictionary<string, ImmutableArray<PromptSuggestion>>.Empty;

        public ImmutableDictionary<SemanticKey, IParamValueExplainer> ParameterExplainers { get; init; } =
            ImmutableDictionary<SemanticKey, IParamValueExplainer>.Empty;

        public ImmutableDictionary<SemanticKey, IParamValueCandidateProvider> ParameterCandidateProviders { get; init; } =
            ImmutableDictionary<SemanticKey, IParamValueCandidateProvider>.Empty;

        public IPromptRuntimeResolver? RuntimeResolver { get; init; }

        internal PromptBufferedAuthoringOptions BufferedAuthoring { get; init; } = new();

        public static PromptSurfaceSpec CreatePlain(string? prompt = null) {
            var bufferedAuthoring = new PromptBufferedAuthoringOptions();
            return new PromptSurfaceSpec {
                Purpose = PromptInputPurpose.Plain,
                Content = CreateContent(
                    string.IsNullOrEmpty(prompt) ? "> " : prompt,
                    bufferedAuthoring: bufferedAuthoring),
                BufferedAuthoring = bufferedAuthoring,
            };
        }

        public static PromptSurfaceSpec CreateCommandLine(string? prompt = null, bool activityActive = false) {
            var bufferedAuthoring = new PromptBufferedAuthoringOptions {
                Keymap = PromptEditorKeymaps.CreateSingleLine(),
            };
            return new PromptSurfaceSpec {
                Purpose = PromptInputPurpose.CommandLine,
                Content = CreateContent(
                    string.IsNullOrEmpty(prompt) ? "> " : prompt,
                    statusBodyLines: PromptEditorKeymaps.CreateCommandStatusBodyLines(multiLine: false, activityActive),
                    bufferedAuthoring: bufferedAuthoring),
                BufferedAuthoring = bufferedAuthoring,
                StaticCandidates = ImmutableDictionary<string, ImmutableArray<PromptSuggestion>>.Empty
                    .Add(PromptSuggestionKindIds.Boolean, PromptSuggestionCatalog.DefaultBooleanSuggestions),
            };
        }

        private static ProjectionDocumentContent CreateContent(
            string prompt,
            IEnumerable<string>? statusBodyLines = null,
            string? ghostText = null,
            EmptySubmitBehavior emptySubmitBehavior = EmptySubmitBehavior.KeepInput,
            bool ctrlEnterBypassesGhost = true,
            PromptBufferedAuthoringOptions? bufferedAuthoring = null) {
            var authoring = bufferedAuthoring ?? new PromptBufferedAuthoringOptions();
            List<ProjectionNodeState> nodes = [
                new TextProjectionNodeState {
                    NodeId = PromptProjectionDocumentFactory.NodeIds.Label,
                    State = new TextNodeState {
                        Content = PromptProjectionDocumentFactory.CreateSingleLineBlock(prompt, SurfaceStyleCatalog.PromptLabel),
                    },
                },
                new EditableTextProjectionNodeState {
                    NodeId = PromptProjectionDocumentFactory.NodeIds.Input,
                    State = new EditableTextNodeState {
                        Decorations = CreateGhostDecorations(ghostText),
                        Submit = PromptProjectionDocumentFactory.CreateSubmitState(
                            emptySubmitBehavior,
                            ctrlEnterBypassesGhost),
                    },
                },
            ];
            var detailLines = PromptProjectionDocumentFactory.CreateBlocks(statusBodyLines, SurfaceStyleCatalog.StatusDetail);
            if (detailLines.Length > 0) {
                nodes.Add(new DetailProjectionNodeState {
                    NodeId = PromptProjectionDocumentFactory.NodeIds.StatusDetail,
                    State = new DetailNodeState {
                        Lines = detailLines,
                    },
                });
            }

            return PromptProjectionDocumentFactory.CreateContent(
                PromptProjectionDocumentFactory.CreateRenderOptions(authoring),
                PromptProjectionDocumentFactory.CreateProjectionStyleDictionary(null),
                nodes: nodes);
        }

        private static ProjectionInlineDecoration[] CreateGhostDecorations(string? ghostText) {
            return string.IsNullOrEmpty(ghostText)
                ? []
                : [
                    new ProjectionInlineDecoration {
                        Kind = ProjectionInlineDecorationKind.GhostText,
                        StartIndex = 0,
                        Content = PromptProjectionDocumentFactory.CreateSingleLineBlock(ghostText),
                    },
                ];
        }
    }

    internal sealed class ResolvedPrompt
    {
        public PromptInputPurpose Purpose { get; set; } = PromptInputPurpose.Plain;
        public ServerContext? Server { get; set; }
        public PromptSemanticSpec? SemanticSpec { get; set; }

        public ImmutableDictionary<string, ImmutableArray<PromptSuggestion>> Candidates { get; set; } =
            ImmutableDictionary<string, ImmutableArray<PromptSuggestion>>.Empty;

        public ImmutableDictionary<SemanticKey, IParamValueExplainer> ParameterExplainers { get; set; } =
            ImmutableDictionary<SemanticKey, IParamValueExplainer>.Empty;

        public ImmutableDictionary<SemanticKey, IParamValueCandidateProvider> ParameterCandidateProviders { get; set; } =
            ImmutableDictionary<SemanticKey, IParamValueCandidateProvider>.Empty;

        public IReadOnlyList<PromptSuggestion> ResolveCandidates(string suggestionKindId) {
            if (Candidates.TryGetValue(suggestionKindId?.Trim() ?? string.Empty, out var resolved)) {
                return resolved;
            }

            return [];
        }

        public IParamValueExplainer? ResolveParameterExplainer(SemanticKey semanticKey) {
            return ParameterExplainers.TryGetValue(semanticKey, out var explainer)
                ? explainer
                : null;
        }

        public IParamValueCandidateProvider? ResolveParameterCandidateProvider(SemanticKey semanticKey) {
            return ParameterCandidateProviders.TryGetValue(semanticKey, out var provider)
                ? provider
                : null;
        }
    }
}
