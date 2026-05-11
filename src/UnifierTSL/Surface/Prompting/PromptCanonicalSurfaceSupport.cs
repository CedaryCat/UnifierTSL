using System.Text;
using UnifierTSL.Surface.Prompting.Model;
using UnifierTSL.Surface.Prompting.Runtime;
using UnifierTSL.Contracts.Display;
using UnifierTSL.Contracts.Projection;
using UnifierTSL.Contracts.Terminal;
using UnifierTSL.TextEditing;

namespace UnifierTSL.Surface.Prompting {
    public static class PromptCanonicalSurfaceSupport {
        internal static EditorMaterialState CreateEditorMaterialState(
            ProjectionDocumentContent authoredContent,
            PromptComputation computation,
            PromptCandidateWindowState candidateWindow,
            PromptInputState inputState,
            EditorSubmitBehavior submitBehavior,
            bool suppressCompletionPreview = false) {
            var label = PromptProjectionDocumentFactory.FindState<TextProjectionNodeState>(
                authoredContent,
                PromptProjectionDocumentFactory.NodeIds.Label,
                EditorProjectionSemanticKeys.InputLabel);
            var sourceText = inputState.InputText ?? string.Empty;
            suppressCompletionPreview |= ShouldSuppressCompletionPreview(computation.InterpretationState);
            return new EditorMaterialState {
                Content = BuildEditorContent(sourceText, computation.InputHighlights),
                Prompt = CreateStyledSegment(
                    string.IsNullOrWhiteSpace(PromptProjectionDocumentFactory.ReadText(label?.State.Content))
                        ? "> "
                        : PromptProjectionDocumentFactory.ReadText(label?.State.Content),
                    SurfaceStyleCatalog.PromptLabel),
                GhostHint = BuildGhostHint(authoredContent, computation, candidateWindow, inputState, suppressCompletionPreview),
                SubmitBehavior = submitBehavior ?? new EditorSubmitBehavior(),
            };
        }

        internal static PromptInterpretationProjectionOverride CreateInterpretationProjectionOverride(PromptInterpretationState? interpretationState) {
            var state = interpretationState ?? PromptInterpretationState.Empty;
            PromptInterpretation[] interpretations = state.Interpretations ?? [];
            if (interpretations.Length == 0) {
                return new PromptInterpretationProjectionOverride();
            }

            int activeOptionIndex = ResolveActiveOptionIndex(state, interpretations);
            PromptInterpretation activeInterpretation = activeOptionIndex >= 0 && activeOptionIndex < interpretations.Length
                ? interpretations[activeOptionIndex]
                : interpretations[0];
            List<InlineSegments> detailLines = [];
            foreach (var section in activeInterpretation?.Sections ?? []) {
                AppendSectionLines(detailLines, section);
            }

            return new PromptInterpretationProjectionOverride {
                Presentation = state.Presentation,
                ActiveInterpretationId = activeInterpretation?.Id ?? string.Empty,
                Summary = CreateStyledSegment(activeInterpretation?.Summary),
                DetailLines = [.. detailLines],
                Options = [.. interpretations
                    .Where(static interpretation => !string.IsNullOrWhiteSpace(interpretation.Id))
                    .Select(static interpretation => new InlineInterpretationOption {
                        Id = interpretation.Id ?? string.Empty,
                        Label = CreateStyledSegment(
                            string.IsNullOrWhiteSpace(interpretation.Label) ? interpretation.Id : interpretation.Label,
                            string.Empty),
                    })],
            };
        }

        public static ProjectionDocumentContent CreateAuthoredContent(
            PromptProjectionRenderOptions renderOptions,
            ProjectionStyleDictionary? styles,
            ProjectionTextBlock? label,
            EditableTextNodeState input,
            IReadOnlyList<PromptCompletionItem>? completionItems,
            string selectedCompletionItemId,
            PromptInterpretationProjectionOverride interpretation,
            IReadOnlyList<ProjectionMarkerCatalogItem>? markerCatalog = null) {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(interpretation);
            var resolvedCompletionItems = completionItems ?? [];
            List<ProjectionNodeState> nodes = [
                new TextProjectionNodeState {
                    NodeId = PromptProjectionDocumentFactory.NodeIds.Label,
                    State = new TextNodeState {
                        Content = label ?? new ProjectionTextBlock(),
                    },
                },
                new EditableTextProjectionNodeState {
                    NodeId = PromptProjectionDocumentFactory.NodeIds.Input,
                    State = input,
                },
                CreateCompletionOptionsNode(resolvedCompletionItems),
                CreateCompletionDetailNode(resolvedCompletionItems, selectedCompletionItemId),
                CreateCompletionMetadataNode(renderOptions),
                CreateInterpretationSummaryNode(interpretation),
                CreateInterpretationOptionsNode(interpretation),
                CreateInterpretationDetailNode(interpretation),
            ];
            if (renderOptions.InputIndicator is not null || renderOptions.InputIndicatorAnimation is not null) {
                nodes.Add(new TextProjectionNodeState {
                    NodeId = PromptProjectionDocumentFactory.NodeIds.InputIndicator,
                    State = new TextNodeState {
                        Content = renderOptions.InputIndicator ?? new ProjectionTextBlock(),
                        Animation = renderOptions.InputIndicatorAnimation,
                    },
                });
            }

            return PromptProjectionDocumentFactory.CreateContent(
                renderOptions,
                styles,
                nodes: [.. nodes],
                selection: CreateSelection(selectedCompletionItemId, interpretation.ActiveInterpretationId),
                markerCatalog: markerCatalog);
        }
        public static InlineSegments CreateStyledSegment(PromptStyledText? text) {
            return text is null
                ? new InlineSegments()
                : CreateStyledSegment(text.Text, text.Highlights, string.Empty);
        }
        public static InlineSegments CreateLabeledStyledSegment(string? label, PromptStyledText line) {
            ArgumentNullException.ThrowIfNull(line);
            if (string.IsNullOrWhiteSpace(label)) {
                return CreateStyledSegment(line);
            }

            string prefix = label + ": ";
            return CreateStyledSegment(new PromptStyledText {
                Text = prefix + line.Text,
                Highlights = [.. line.Highlights.Select(highlight => new PromptHighlightSpan(
                    highlight.StartIndex + prefix.Length,
                    highlight.Length,
                    highlight.StyleId))],
            });
        }

        public static InlineSegments CreateStyledSegment(string? text, string? styleId) {
            return CreateStyledSegment(text ?? string.Empty, [], styleId ?? string.Empty);
        }

        public static InlineSegments CreateStyledSegment(
            string? text,
            IReadOnlyList<PromptHighlightSpan>? highlights,
            string? fallbackStyleId) {
            string value = text ?? string.Empty;
            if (value.Length == 0) {
                return new InlineSegments {
                    Text = string.Empty,
                    Highlights = [],
                };
            }

            string effectiveFallbackStyleId = fallbackStyleId ?? string.Empty;
            string[] styleIds = new string[value.Length];
            if (!string.IsNullOrWhiteSpace(effectiveFallbackStyleId)) {
                for (int index = 0; index < styleIds.Length; index++) {
                    styleIds[index] = effectiveFallbackStyleId;
                }
            }

            foreach (var highlight in highlights ?? []) {
                if (highlight.Length <= 0 || string.IsNullOrWhiteSpace(highlight.StyleId)) {
                    continue;
                }

                int start = Math.Clamp(highlight.StartIndex, 0, value.Length);
                int end = Math.Clamp(highlight.EndIndex, start, value.Length);
                if (end <= start) {
                    continue;
                }

                for (int index = start; index < end; index++) {
                    styleIds[index] = highlight.StyleId;
                }
            }

            List<HighlightSpan> resolvedHighlights = [];
            int runStart = 0;
            string activeStyleId = styleIds[0];
            for (int index = 1; index <= styleIds.Length; index++) {
                bool reachedEnd = index >= styleIds.Length;
                bool styleChanged = !reachedEnd && !string.Equals(styleIds[index], activeStyleId, StringComparison.Ordinal);
                if (!reachedEnd && !styleChanged) {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(activeStyleId) && index > runStart) {
                    resolvedHighlights.Add(new HighlightSpan {
                        StartIndex = runStart,
                        Length = index - runStart,
                        StyleId = activeStyleId,
                    });
                }

                if (reachedEnd) {
                    break;
                }

                runStart = index;
                activeStyleId = styleIds[index];
            }

            return new InlineSegments {
                Text = value,
                Highlights = [.. resolvedHighlights],
            };
        }

        private static CollectionProjectionNodeState CreateCompletionOptionsNode(IReadOnlyList<PromptCompletionItem> items) {
            return new CollectionProjectionNodeState {
                NodeId = PromptProjectionDocumentFactory.NodeIds.CompletionOptions,
                State = new CollectionNodeState {
                    Items = [.. items.Select(CreateCompletionItem)],
                    TotalItemCount = items.Count,
                    PageSize = items.Count == 0 ? 0 : items.Count,
                },
            };
        }

        private static DetailProjectionNodeState CreateCompletionDetailNode(
            IReadOnlyList<PromptCompletionItem> items,
            string selectedCompletionItemId) {
            return new DetailProjectionNodeState {
                NodeId = PromptProjectionDocumentFactory.NodeIds.CompletionDetail,
                State = new DetailNodeState {
                    ContextItemId = selectedCompletionItemId,
                    Lines = ResolveCompletionDetail(items, selectedCompletionItemId),
                },
            };
        }
        private static PropertySetProjectionNodeState CreateCompletionMetadataNode(PromptProjectionRenderOptions renderOptions) {
            return new PropertySetProjectionNodeState {
                NodeId = PromptProjectionDocumentFactory.NodeIds.CompletionMetadata,
                State = new PropertySetNodeState {
                    Properties = [
                        new ProjectionPropertyValue {
                            PropertyKey = EditorProjectionMetadataKeys.AssistPrimaryActivationMode,
                            Value = PromptProjectionDocumentFactory.CreateSingleLineBlock(
                                renderOptions.CompletionActivationMode == CompletionActivationMode.Automatic
                                    ? "automatic"
                                    : "manual"),
                        },
                    ],
                },
            };
        }

        private static TextProjectionNodeState CreateInterpretationSummaryNode(PromptInterpretationProjectionOverride interpretation) {
            return new TextProjectionNodeState {
                NodeId = PromptProjectionDocumentFactory.NodeIds.InterpretationSummary,
                State = new TextNodeState {
                    Content = PromptProjectionDocumentFactory.ResolveInterpretationSummary(interpretation),
                },
            };
        }
        private static CollectionProjectionNodeState CreateInterpretationOptionsNode(PromptInterpretationProjectionOverride interpretation) {
            return new CollectionProjectionNodeState {
                NodeId = PromptProjectionDocumentFactory.NodeIds.InterpretationOptions,
                State = new CollectionNodeState {
                    Items = [.. interpretation.Options.Select(option => new ProjectionCollectionItem {
                        ItemId = option.Id,
                        Label = PromptProjectionDocumentFactory.ToBlock(option.Label),
                    })],
                    TotalItemCount = (interpretation.Options ?? []).Length,
                },
            };
        }
        private static DetailProjectionNodeState CreateInterpretationDetailNode(PromptInterpretationProjectionOverride interpretation) {
            return new DetailProjectionNodeState {
                NodeId = PromptProjectionDocumentFactory.NodeIds.InterpretationDetail,
                State = new DetailNodeState {
                    ContextItemId = interpretation.ActiveInterpretationId,
                    Lines = PromptProjectionDocumentFactory.ToBlocks(interpretation.DetailLines),
                },
            };
        }
        private static ProjectionCollectionItem CreateCompletionItem(PromptCompletionItem item) {
            return new ProjectionCollectionItem {
                ItemId = item.Id,
                Label = CreateBlock(CreateStyledSegment(
                    item.DisplayText,
                    item.DisplayHighlights,
                    item.DisplayStyleId)),
                SecondaryLabel = CreateBlock(CreateStyledSegment(
                    item.SecondaryDisplayText,
                    item.SecondaryDisplayHighlights,
                    item.SecondaryDisplayStyleId)),
                TrailingLabel = CreateBlock(CreateStyledSegment(
                    item.TrailingDisplayText,
                    item.TrailingDisplayHighlights,
                    item.TrailingDisplayStyleId)),
                Summary = CreateBlock(CreateStyledSegment(
                    item.SummaryText,
                    item.SummaryHighlights,
                    item.SummaryStyleId)),
                PrimaryEdit = new ProjectionTextEditOperation {
                    TargetNodeId = PromptProjectionDocumentFactory.NodeIds.Input,
                    StartIndex = item.PrimaryEdit.StartIndex,
                    Length = item.PrimaryEdit.Length,
                    NewText = item.PrimaryEdit.NewText,
                },
            };
        }
        private static ProjectionTextBlock[] ResolveCompletionDetail(
            IReadOnlyList<PromptCompletionItem> items,
            string selectedCompletionItemId) {
            var selected = string.IsNullOrWhiteSpace(selectedCompletionItemId)
                ? null
                : items.FirstOrDefault(item => string.Equals(item.Id, selectedCompletionItemId, StringComparison.Ordinal));
            return selected is null || string.IsNullOrWhiteSpace(selected.SummaryText)
                ? []
                : [
                    CreateBlock(CreateStyledSegment(
                        selected.SummaryText,
                        selected.SummaryHighlights,
                        selected.SummaryStyleId)),
                ];
        }
        private static ProjectionSelectionSet CreateSelection(
            string selectedCompletionItemId,
            string activeInterpretationId) {
            List<ProjectionNodeSelectionState> nodes = [];
            if (!string.IsNullOrWhiteSpace(selectedCompletionItemId)) {
                nodes.Add(new ProjectionNodeSelectionState {
                    NodeId = PromptProjectionDocumentFactory.NodeIds.CompletionOptions,
                    ActiveItemId = selectedCompletionItemId,
                    SelectedItemIds = [selectedCompletionItemId],
                });
            }

            if (!string.IsNullOrWhiteSpace(activeInterpretationId)) {
                nodes.Add(new ProjectionNodeSelectionState {
                    NodeId = PromptProjectionDocumentFactory.NodeIds.InterpretationOptions,
                    ActiveItemId = activeInterpretationId,
                    SelectedItemIds = [activeInterpretationId],
                });
            }

            return nodes.Count == 0
                ? new ProjectionSelectionSet()
                : new ProjectionSelectionSet {
                    Nodes = [.. nodes],
                };
        }

        public static ProjectionTextBlock CreateBlock(InlineSegments? segments) {
            return PromptProjectionDocumentFactory.ToBlock(segments);
        }

        private static InlineSegments ToInlineSegments(ProjectionTextBlock? block) {
            if (block?.Lines is not { Length: > 0 } lines) {
                return new InlineSegments();
            }

            List<HighlightSpan> highlights = [];
            StringBuilder builder = new();
            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++) {
                if (lineIndex > 0) {
                    builder.Append('\n');
                }

                foreach (var span in lines[lineIndex].Spans ?? []) {
                    string text = span?.Text ?? string.Empty;
                    if (text.Length == 0) {
                        continue;
                    }

                    int start = builder.Length;
                    builder.Append(text);
                    if (span?.Style is { } style && !string.IsNullOrWhiteSpace(style.Key)) {
                        highlights.Add(new HighlightSpan {
                            StartIndex = start,
                            Length = text.Length,
                            StyleId = style.Key,
                        });
                    }
                }
            }

            return new InlineSegments {
                Text = builder.ToString(),
                Highlights = [.. highlights],
            };
        }

        private static bool ShouldSuppressCompletionPreview(PromptInterpretationState? interpretationState) {
            return interpretationState is { Interpretations.Length: > 0 }
                && (interpretationState.Presentation?.SuppressesCompletionPreview ?? false);
        }

        private static InlineSegments BuildEditorContent(string sourceText, IReadOnlyList<PromptHighlightSpan> highlights) {
            return CreateStyledSegment(sourceText, highlights, SurfaceStyleCatalog.PromptInput);
        }

        private static GhostInlineHint BuildGhostHint(
            ProjectionDocumentContent authoredContent,
            PromptComputation computation,
            PromptCandidateWindowState candidateWindow,
            PromptInputState inputState,
            bool suppressCompletionPreview) {
            if (!suppressCompletionPreview
                && TryCreateCompletionInsertions(computation, candidateWindow, inputState, out string sourceCompletionId, out InlineTextInsertion[] insertions)) {
                return new GhostInlineHint {
                    SourceCompletionId = sourceCompletionId,
                    Insertions = insertions,
                };
            }

            var input = PromptProjectionDocumentFactory.FindState<EditableTextProjectionNodeState>(
                authoredContent,
                PromptProjectionDocumentFactory.NodeIds.Input,
                EditorProjectionSemanticKeys.Input);
            string sourceText = inputState.InputText ?? string.Empty;
            var authoredGhost = input?.State.Decorations?
                .FirstOrDefault(static decoration => decoration.Kind == ProjectionInlineDecorationKind.GhostText);
            string ghostText = PromptProjectionDocumentFactory.ReadText(authoredGhost?.Content);
            bool shouldUseAuthoredGhost = !string.IsNullOrWhiteSpace(ghostText)
                && sourceText.Length == 0
                && inputState.CursorIndex == 0;
            if (shouldUseAuthoredGhost) {
                var ghostSegments = ToInlineSegments(authoredGhost?.Content);
                return new GhostInlineHint {
                    Insertions = [
                        new InlineTextInsertion {
                            SourceIndex = Math.Max(0, authoredGhost?.StartIndex ?? 0),
                            Content = InlineSegmentsOps.HasVisibleText(ghostSegments)
                                ? ghostSegments
                                : CreateStyledSegment(ghostText, SurfaceStyleCatalog.InlineHint),
                        },
                    ],
                };
            }

            return new GhostInlineHint();
        }

        private static bool TryCreateCompletionInsertions(
            PromptComputation computation,
            PromptCandidateWindowState candidateWindow,
            PromptInputState inputState,
            out string sourceCompletionId,
            out InlineTextInsertion[] insertions) {
            sourceCompletionId = string.Empty;
            insertions = [];
            string sourceText = inputState.InputText ?? string.Empty;
            if (!LogicalLineBounds.IsCursorAtEnd(sourceText, inputState.CursorIndex)) {
                return false;
            }

            PromptCompletionItem? completion = ResolveDisplayCompletion(computation, candidateWindow, inputState);
            if (completion is null || !PromptInlinePreview.TryCreateInsertions(sourceText, completion, out PromptInlinePreviewInsertion[] preview)) {
                return false;
            }

            insertions = [.. preview.Select(static insertion => new InlineTextInsertion {
                SourceIndex = insertion.SourceIndex,
                Content = CreateStyledSegment(
                    insertion.Text,
                    string.IsNullOrWhiteSpace(insertion.StyleId) ? SurfaceStyleCatalog.InlineHint : insertion.StyleId),
            })];
            sourceCompletionId = ResolveCompletionId(completion, sourceText);
            return insertions.Length > 0;
        }

        private static PromptCompletionItem? ResolveDisplayCompletion(
            PromptComputation computation,
            PromptCandidateWindowState candidateWindow,
            PromptInputState inputState) {
            PromptCompletionItem[] candidates = PromptCandidateWindowProjector.ResolveDisplayedCandidates(computation, candidateWindow);
            if (candidates.Length == 0) {
                return null;
            }

            if (inputState.CompletionIndex > 0) {
                int localIndex = candidateWindow.IsPaged
                    ? inputState.CompletionIndex - Math.Max(0, candidateWindow.WindowOffset) - 1
                    : inputState.CompletionIndex - 1;
                if (localIndex >= 0 && localIndex < candidates.Length) {
                    return candidates[localIndex];
                }
            }

            string preferredCompletionText = string.IsNullOrWhiteSpace(inputState.PreferredCompletionText)
                ? computation.PreferredCompletionText ?? string.Empty
                : inputState.PreferredCompletionText;
            if (!string.IsNullOrWhiteSpace(preferredCompletionText)) {
                string sourceText = inputState.InputText ?? string.Empty;
                return candidates.FirstOrDefault(candidate =>
                    string.Equals(
                        candidate.PrimaryEdit.Apply(sourceText),
                        preferredCompletionText,
                        StringComparison.OrdinalIgnoreCase)
                    && PromptInlinePreview.TryCreateInsertions(sourceText, candidate, out _));
            }

            return candidates[0];
        }

        private static string ResolveCompletionId(PromptCompletionItem candidate, string sourceText) {
            if (!string.IsNullOrWhiteSpace(candidate.Id)) {
                return candidate.Id;
            }

            string resolved = candidate.PrimaryEdit.Apply(sourceText);
            return string.IsNullOrWhiteSpace(resolved)
                ? candidate.DisplayText ?? string.Empty
                : resolved;
        }

        private static int ResolveActiveOptionIndex(PromptInterpretationState interpretationState, IReadOnlyList<PromptInterpretation> interpretations) {
            if (!string.IsNullOrWhiteSpace(interpretationState.ActiveInterpretationId)) {
                for (int index = 0; index < interpretations.Count; index++) {
                    if (string.Equals(interpretations[index].Id, interpretationState.ActiveInterpretationId, StringComparison.Ordinal)) {
                        return index;
                    }
                }
            }

            if (interpretationState.ActiveInterpretationIndex >= 0
                && interpretationState.ActiveInterpretationIndex < interpretations.Count) {
                return interpretationState.ActiveInterpretationIndex;
            }

            return interpretations.Count == 0 ? -1 : 0;
        }

        private static void AppendSectionLines(List<InlineSegments> lines, PromptInterpretationSection section) {
            PromptStyledText[] sectionLines = section.Lines ?? [];
            if (sectionLines.Length == 0) {
                AppendDistinctLine(lines, section.Label, SurfaceStyleCatalog.InlineHint);
                return;
            }

            for (int index = 0; index < sectionLines.Length; index++) {
                PromptStyledText line = sectionLines[index];
                if (string.IsNullOrWhiteSpace(line?.Text)) {
                    continue;
                }

                if (index == 0 && !string.IsNullOrWhiteSpace(section.Label)) {
                    AppendDistinctLine(lines, CreateLabeledStyledSegment(section.Label, line));
                    continue;
                }

                AppendDistinctLine(lines, CreateStyledSegment(line));
            }
        }

        private static void AppendDistinctLine(List<InlineSegments> lines, string? text, string styleId) {
            if (string.IsNullOrWhiteSpace(text)) {
                return;
            }

            if (lines.Any(line => string.Equals(line.Text, text, StringComparison.OrdinalIgnoreCase))) {
                return;
            }

            lines.Add(CreateStyledSegment(text, styleId));
        }

        private static void AppendDistinctLine(List<InlineSegments> lines, InlineSegments line) {
            if (!InlineSegmentsOps.HasVisibleText(line)) {
                return;
            }

            if (lines.Any(existing => string.Equals(existing.Text, line.Text, StringComparison.OrdinalIgnoreCase))) {
                return;
            }

            lines.Add(line);
        }

    }
}
