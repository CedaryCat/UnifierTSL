using System.Collections.Immutable;
using UnifierTSL.Surface.Prompting.Model;
using UnifierTSL.Surface.Prompting;
using UnifierTSL.Contracts.Display;
using UnifierTSL.Contracts.Projection;
using UnifierTSL.Contracts.Terminal;

namespace UnifierTSL.Surface.Prompting.Startup
{
    internal static class LauncherStartupPromptFactory
    {
        public static PromptSurfaceSpec CreateListenPortPrompt(
            string? lastError,
            IReadOnlyList<int> portCandidates,
            Func<PromptInputState, IReadOnlyList<PromptSuggestion>> suggestionResolver,
            Func<PromptInputState, IReadOnlyList<string>> statusResolver) {

            List<string> baseStatusBodyLines = [
                GetString("use Tab to rotate; Right to accept"),
                GetString("Enter on empty uses ghost; Ctrl+Enter keeps raw input."),
                GetString("Select the launcher listen port"),
            ];
            if (!string.IsNullOrWhiteSpace(lastError)) {
                baseStatusBodyLines.Add(GetParticularString("{0} is error reason", $"last error: {lastError}"));
            }

            var bufferedAuthoring = new PromptBufferedAuthoringOptions();
            var content = CreatePromptContent(
                bufferedAuthoring,
                "listen-port> ",
                GetString("Setup Listen Port"),
                baseStatusBodyLines,
                [.. portCandidates.Select(static port => new PromptSuggestion(port.ToString(), 0))],
                "7777",
                EmptySubmitBehavior.AcceptGhostIfAvailable,
                ctrlEnterBypassesGhost: true);
            return new PromptSurfaceSpec {
                Purpose = PromptInputPurpose.StartupPort,
                Content = content,
                BufferedAuthoring = bufferedAuthoring,
                RuntimeResolver = new StickyPrefixPromptResolver(content, suggestionResolver, statusResolver),
            };
        }

        public static PromptSurfaceSpec CreateServerPasswordPrompt(
            IReadOnlyList<string> passwordCandidates,
            Func<PromptInputState, IReadOnlyList<PromptSuggestion>> suggestionResolver,
            Func<PromptInputState, IReadOnlyList<string>> statusResolver) {

            var bufferedAuthoring = new PromptBufferedAuthoringOptions();
            var content = CreatePromptContent(
                bufferedAuthoring,
                "server-password> ",
                GetString("Setup Server Password"),
                [
                    GetString("use Tab to rotate; Right to accept"),
                    GetString("Press Enter to keep your current input (can be empty)."),
                    GetString("Pick a short startup password (plain input)."),
                ],
                [.. passwordCandidates.Select(static value => new PromptSuggestion(value, 0))],
                passwordCandidates.Count > 0 ? passwordCandidates[0] : string.Empty,
                EmptySubmitBehavior.KeepInput,
                ctrlEnterBypassesGhost: true);
            return new PromptSurfaceSpec {
                Purpose = PromptInputPurpose.StartupPassword,
                Content = content,
                BufferedAuthoring = bufferedAuthoring,
                RuntimeResolver = new StickyPrefixPromptResolver(content, suggestionResolver, statusResolver),
            };
        }

        private static long BuildRuntimeRevision(
            IReadOnlyList<PromptSuggestion> suggestions,
            IReadOnlyList<string> statusLines) {
            HashCode hash = new();
            foreach (var suggestion in suggestions ?? []) {
                if (string.IsNullOrWhiteSpace(suggestion.Value)) {
                    continue;
                }

                hash.Add(suggestion.Value.Trim(), StringComparer.OrdinalIgnoreCase);
                hash.Add(suggestion.Weight);
            }

            foreach (var statusLine in statusLines ?? []) {
                if (string.IsNullOrWhiteSpace(statusLine)) {
                    continue;
                }

                hash.Add(statusLine.Trim(), StringComparer.Ordinal);
            }

            return hash.ToHashCode();
        }

        private static ProjectionDocumentContent CreatePromptContent(
            PromptBufferedAuthoringOptions bufferedAuthoring,
            string prompt,
            string inputSummary,
            IEnumerable<string> statusBodyLines,
            IReadOnlyList<PromptSuggestion> suggestions,
            string ghostText,
            EmptySubmitBehavior emptySubmitBehavior,
            bool ctrlEnterBypassesGhost) {
            return PromptProjectionDocumentFactory.CreateContent(
                PromptProjectionDocumentFactory.CreateRenderOptions(bufferedAuthoring),
                PromptProjectionDocumentFactory.CreateProjectionStyleDictionary(null),
                nodes: [
                    new TextProjectionNodeState {
                        NodeId = PromptProjectionDocumentFactory.NodeIds.Label,
                        State = new TextNodeState {
                            Content = PromptProjectionDocumentFactory.CreateSingleLineBlock(prompt, SurfaceStyleCatalog.PromptLabel),
                        },
                    },
                    CreateInputNode(ghostText, emptySubmitBehavior, ctrlEnterBypassesGhost),
                    new TextProjectionNodeState {
                        NodeId = PromptProjectionDocumentFactory.NodeIds.StatusSummary,
                        State = new TextNodeState {
                            Content = PromptProjectionDocumentFactory.CreateSingleLineBlock(inputSummary, SurfaceStyleCatalog.StatusSummary),
                        },
                    },
                    CreateStatusDetailNode(statusBodyLines),
                    CreateCompletionNode(suggestions),
                ]);
        }

        private static EditableTextProjectionNodeState CreateInputNode(
            string ghostText,
            EmptySubmitBehavior emptySubmitBehavior,
            bool ctrlEnterBypassesGhost) {
            return new EditableTextProjectionNodeState {
                NodeId = PromptProjectionDocumentFactory.NodeIds.Input,
                State = new EditableTextNodeState {
                    Decorations = string.IsNullOrEmpty(ghostText)
                        ? []
                        : [
                            new ProjectionInlineDecoration {
                                Kind = ProjectionInlineDecorationKind.GhostText,
                                StartIndex = 0,
                                Content = PromptProjectionDocumentFactory.CreateSingleLineBlock(ghostText),
                            },
                        ],
                    Submit = PromptProjectionDocumentFactory.CreateSubmitState(
                        emptySubmitBehavior,
                        ctrlEnterBypassesGhost),
                },
            };
        }

        private static DetailProjectionNodeState CreateStatusDetailNode(IEnumerable<string> statusBodyLines) {
            return new DetailProjectionNodeState {
                NodeId = PromptProjectionDocumentFactory.NodeIds.StatusDetail,
                State = new DetailNodeState {
                    Lines = PromptProjectionDocumentFactory.CreateBlocks(statusBodyLines, SurfaceStyleCatalog.StatusDetail),
                },
            };
        }

        private static CollectionProjectionNodeState CreateCompletionNode(IReadOnlyList<PromptSuggestion> suggestions) {
            return new CollectionProjectionNodeState {
                NodeId = PromptProjectionDocumentFactory.NodeIds.CompletionOptions,
                State = new CollectionNodeState {
                    Items = [.. (suggestions ?? [])
                        .Where(static suggestion => !string.IsNullOrWhiteSpace(suggestion.Value))
                        .Select(CreateCompletionItem)],
                    TotalItemCount = suggestions?.Count ?? 0,
                },
            };
        }

        private static ProjectionCollectionItem CreateCompletionItem(PromptSuggestion suggestion) {
            return new ProjectionCollectionItem {
                ItemId = suggestion.Value,
                Label = PromptProjectionDocumentFactory.CreateSingleLineBlock(suggestion.Value),
                PrimaryEdit = new ProjectionTextEditOperation {
                    TargetNodeId = PromptProjectionDocumentFactory.NodeIds.Input,
                    NewText = suggestion.Value,
                },
            };
        }

        private sealed class StickyPrefixPromptResolver(
            ProjectionDocumentContent baseContent,
            Func<PromptInputState, IReadOnlyList<PromptSuggestion>> suggestionResolver,
            Func<PromptInputState, IReadOnlyList<string>> statusResolver) : IPromptRuntimeResolver
        {
            private readonly Lock sync = new();
            private readonly EmptySubmitBehavior emptySubmitBehavior = ResolveEmptySubmitBehavior(baseContent);
            private readonly bool ctrlEnterBypassesGhost = ResolveCtrlEnterBypassesGhost(baseContent);
            private ImmutableArray<PromptSuggestion> stickyFamily = [];
            private string stickyGhost = string.Empty;

            public long GetRevision(PromptResolveContext context) {
                lock (sync) {
                    var resolution = Resolve(context.State, mutateState: false);
                    return BuildRuntimeRevision(resolution.VisibleSuggestions, resolution.StatusLines);
                }
            }

            public ProjectionDocumentContent Resolve(PromptResolveContext context) {
                lock (sync) {
                    var resolution = Resolve(context.State, mutateState: true);
                    return PromptProjectionDocumentFactory.WithState(
                        baseContent,
                        nodes: [
                            CreateInputNode(
                                resolution.GhostText,
                                emptySubmitBehavior,
                                ctrlEnterBypassesGhost),
                            CreateStatusDetailNode(resolution.StatusLines),
                            CreateCompletionNode(resolution.VisibleSuggestions),
                        ]);
                }
            }

            private PasswordPromptResolution Resolve(PromptInputState state, bool mutateState) {
                var input = state.InputText ?? string.Empty;

                ImmutableArray<PromptSuggestion> family;
                ImmutableArray<PromptSuggestion> visibleSuggestions;
                if (string.IsNullOrEmpty(input)) {
                    family = [.. suggestionResolver(CreateResolverState(string.Empty))];
                    visibleSuggestions = family;
                }
                else {
                    var canReuseStickyFamily = !string.IsNullOrWhiteSpace(stickyGhost)
                        && stickyGhost.StartsWith(input, StringComparison.OrdinalIgnoreCase)
                        && !stickyFamily.IsDefaultOrEmpty;
                    family = canReuseStickyFamily
                        ? stickyFamily
                        : [.. suggestionResolver(CreateResolverState(input))];
                    visibleSuggestions = FilterSuggestionsByPrefix(family, input);
                    if (visibleSuggestions.IsDefaultOrEmpty) {
                        family = [.. suggestionResolver(CreateResolverState(input))];
                        visibleSuggestions = FilterSuggestionsByPrefix(family, input);
                    }
                }

                ImmutableArray<string> statusLines = [.. statusResolver(state)];
                var ghostText = ResolveSelectedSuggestionValue(state, visibleSuggestions) ?? string.Empty;

                if (mutateState) {
                    stickyFamily = family;
                    stickyGhost = ghostText;
                }

                return new PasswordPromptResolution(visibleSuggestions, ghostText, statusLines);
            }

            private static EmptySubmitBehavior ResolveEmptySubmitBehavior(ProjectionDocumentContent content) {
                return ResolveSubmitState(content).EmptyInputAction == ProjectionEmptySubmitAction.AcceptPreviewIfAvailable
                    ? EmptySubmitBehavior.AcceptGhostIfAvailable
                    : EmptySubmitBehavior.KeepInput;
            }

            private static bool ResolveCtrlEnterBypassesGhost(ProjectionDocumentContent content) {
                return ResolveSubmitState(content).AlternateSubmitBypassesPreview;
            }

            private static ProjectionSubmitState ResolveSubmitState(ProjectionDocumentContent content) {
                return PromptProjectionDocumentFactory.FindState<EditableTextProjectionNodeState>(
                    content,
                    PromptProjectionDocumentFactory.NodeIds.Input,
                    EditorProjectionSemanticKeys.Input)?.State.Submit ?? new ProjectionSubmitState();
            }

            private static PromptInputState CreateResolverState(string inputText) {
                return new PromptInputState {
                    InputText = inputText,
                    CursorIndex = inputText.Length,
                    CompletionIndex = 0,
                    CompletionCount = 0,
                    CandidateWindowOffset = 0,
                };
            }

            private static ImmutableArray<PromptSuggestion> FilterSuggestionsByPrefix(
                IEnumerable<PromptSuggestion> suggestions,
                string input) {
                return [.. (suggestions ?? [])
                    .Where(suggestion => !string.IsNullOrWhiteSpace(suggestion.Value)
                        && suggestion.Value.StartsWith(input, StringComparison.OrdinalIgnoreCase))];
            }

            private static string? ResolveSelectedSuggestionValue(
                PromptInputState state,
                IReadOnlyList<PromptSuggestion> suggestions) {
                if (suggestions is null || suggestions.Count == 0) {
                    return null;
                }

                var selectedOrdinal = state.CompletionIndex;
                if (selectedOrdinal > 0 && selectedOrdinal <= suggestions.Count) {
                    return suggestions[selectedOrdinal - 1].Value;
                }

                return suggestions[0].Value;
            }

            private readonly record struct PasswordPromptResolution(
                ImmutableArray<PromptSuggestion> VisibleSuggestions,
                string GhostText,
                ImmutableArray<string> StatusLines);
        }
    }
}
