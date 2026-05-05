using System.Text;
using UnifierTSL.Contracts.Display;
using UnifierTSL.Contracts.Projection;
using UnifierTSL.Contracts.Sessions;
using UnifierTSL.Contracts.Terminal.Overlay;
using UnifierTSL.Terminal;
using UnifierTSL.Terminal.Overlay;
using UnifierTSL.Terminal.Runtime;
using UnifierTSL.TextEditing;

namespace UnifierTSL.Terminal.Shell
{
    internal sealed class TerminalRenderScene
    {
        public TerminalRenderInput Input { get; init; } = new();
        public IReadOnlyList<StatusRenderLine> StatusLines { get; init; } = [];
        public IReadOnlyList<StatusRenderLine> InlineAssistLines { get; init; } = [];
        public StyleDictionary StyleDictionary { get; init; } = new();
        public PanePopupMaterial? PopupAssist { get; init; }
        public int StatusScrollOffset { get; init; }
    }

    internal sealed class TerminalRenderInput
    {
        public string PromptText { get; init; } = "> ";
        public ushort[] PromptStyleSlots { get; init; } = [];
        public string IndicatorText { get; init; } = string.Empty;
        public ushort[] IndicatorStyleSlots { get; init; } = [];
        public string DisplayText { get; init; } = string.Empty;
        public ushort[] DisplayStyleSlots { get; init; } = [];
        public int CursorCompositeIndex { get; init; }
        public bool ShowCompletionBadge { get; init; }
        public int CompletionIndex { get; init; }
        public int CompletionCount { get; init; }
        public ushort[] CompletionBadgeStyleSlots { get; init; } = [];
    }

    internal sealed class TerminalAnimatedTextPlayback
    {
        public int FrameStepTicks { get; init; }
        public StyledTextLine[] Frames { get; init; } = [];
        public long FrameAnchorTick { get; init; }
        public int LastFrameStep { get; set; } = -1;
        public StyledTextLine RenderedFrame { get; set; } = new();
    }

    internal sealed class TerminalStatusBarState
    {
        public TerminalStatusBarState(
            long sequence,
            TerminalSurfaceRuntimeFrame frame,
            TerminalAnimatedTextPlayback indicator) {
            Sequence = sequence;
            Frame = frame ?? throw new ArgumentNullException(nameof(frame));
            Indicator = indicator;
        }

        public long Sequence;
        public readonly TerminalSurfaceRuntimeFrame Frame;
        public readonly TerminalAnimatedTextPlayback Indicator;
    }

    internal sealed class TerminalTuiAdapter
    {
        private const int MaxStatusBodyLines = 5;
        private const int PopupCompletionMaxRows = 9;
        private const int PopupHorizontalPadding = 1;

        public TerminalRenderScene BuildRenderScene(
            TerminalSurfaceRuntimeFrame frame,
            LineEditorRenderState inputState,
            TerminalStatusBarState? statusBar,
            StyledTextLine inputIndicator,
            int requestedScrollOffset,
            int writableWidth) {
            var statusFrame = BuildStatusFrame(frame, inputState.Overlay, inputState.OverlayPlan, statusBar, requestedScrollOffset);
            return new TerminalRenderScene {
                Input = BuildRenderInput(frame, inputState, inputIndicator),
                StatusLines = statusFrame.Lines,
                InlineAssistLines = BuildInlineAssistOverlayLines(
                    frame,
                    inputState,
                    writableWidth),
                StyleDictionary = MergeRenderStyleDictionaries(
                    frame.StyleDictionary,
                    statusFrame.StyleDictionary),
                PopupAssist = BuildPopupAssistMaterial(
                    frame,
                    inputState,
                    writableWidth),
                StatusScrollOffset = statusFrame.ScrollOffset,
            };
        }

        public IReadOnlyList<StatusRenderLine> BuildFallbackStatusLines(
            TerminalSurfaceRuntimeFrame frame,
            LineEditorRenderState inputState,
            TerminalStatusBarState? statusBar) {
            return BuildStatusFrame(frame, inputState.Overlay, inputState.OverlayPlan, statusBar, 0).Lines;
        }

        private static StyleDictionary MergeRenderStyleDictionaries(params StyleDictionary?[] dictionaries) {
            Dictionary<string, StyledTextStyle> stylesById = new(StringComparer.Ordinal);
            foreach (var dictionary in dictionaries ?? []) {
                foreach (var style in dictionary?.Styles ?? []) {
                    if (style is null || string.IsNullOrWhiteSpace(style.StyleId) || stylesById.ContainsKey(style.StyleId)) {
                        continue;
                    }

                    stylesById[style.StyleId] = style;
                }
            }

            return new StyleDictionary {
                Styles = [.. stylesById.Values],
            };
        }

        public bool HasStatusContent(
            TerminalSurfaceRuntimeFrame frame,
            LineEditorRenderState inputState,
            TerminalStatusBarState? statusBar) {
            if (statusBar is not null) {
                return true;
            }

            if (StyledTextLineOps.HasVisibleText(BuildInteractionStatusLine(frame, inputState.Overlay, inputState.OverlayPlan, statusBarSurface: null))) {
                return true;
            }

            return BuildStatusContentLines(frame, inputState.Overlay, inputState.OverlayPlan).Any(HasVisibleStatusText);
        }

        public int ApplyStatusScrollDelta(
            TerminalSurfaceRuntimeFrame frame,
            LineEditorRenderState inputState,
            TerminalStatusBarState? statusBar,
            int currentScrollOffset,
            int delta) {
            if (delta == 0) {
                return currentScrollOffset;
            }

            var pinnedLineCount = GetPinnedStatusDetailLines(statusBar).Count(StyledTextLineOps.HasVisibleText);
            var remainingBodySlots = Math.Max(0, MaxStatusBodyLines - Math.Min(MaxStatusBodyLines, pinnedLineCount));
            var totalBodyLines = BuildStatusContentLines(frame, inputState.Overlay, inputState.OverlayPlan).Count;
            var maxScroll = remainingBodySlots <= 0
                ? 0
                : Math.Max(0, totalBodyLines - remainingBodySlots);
            return Math.Clamp(currentScrollOffset + delta, 0, maxScroll);
        }

        public (string Text, ushort[] StyleSlots) ResolvePrompt(TerminalSurfaceRuntimeFrame frame) {
            var prompt = TerminalProjectionInputAdapter.ResolvePrompt(frame);
            var text = prompt?.Text ?? string.Empty;
            if (string.IsNullOrEmpty(text)) {
                text = "> ";
            }

            return (text, BuildContentStyleSlots(text, prompt ?? new InlineSegments { Text = text }, frame.StyleDictionary));
        }

        private TerminalRenderInput BuildRenderInput(
            TerminalSurfaceRuntimeFrame frame,
            LineEditorRenderState inputState,
            StyledTextLine inputIndicator) {
            var composition = BuildDisplayComposition(inputState, frame, TerminalProjectionInputAdapter.ResolveContent(frame));
            var (promptText, promptStyleSlots) = ResolvePrompt(frame);
            var (indicatorText, indicatorStyleSlots) = CreateIndicatorPrefix(
                inputState.OverlayPlan.InputIndicatorControl is null ? new StyledTextLine() : inputIndicator,
                frame.StyleDictionary);
            return new TerminalRenderInput {
                PromptText = promptText,
                PromptStyleSlots = promptStyleSlots,
                IndicatorText = indicatorText,
                IndicatorStyleSlots = indicatorStyleSlots,
                DisplayText = composition.Text,
                DisplayStyleSlots = composition.StyleSlots,
                CursorCompositeIndex = composition.CursorCompositeIndex,
                ShowCompletionBadge = inputState.OverlayPlan.CompletionControl?.AnchorTarget != OverlayAnchorTarget.Caret,
                CompletionIndex = inputState.CompletionIndex,
                CompletionCount = inputState.CompletionCount,
                CompletionBadgeStyleSlots = BuildUniformStyleSlots(
                    $" [{inputState.CompletionIndex}/{inputState.CompletionCount}]",
                    TerminalTextLayoutOps.ResolveStyleSlot(frame.StyleDictionary, SurfaceStyleCatalog.PromptCompletionBadge)),
            };
        }

        private static (string Text, ushort[] StyleSlots) CreateIndicatorPrefix(StyledTextLine indicator, StyleDictionary styleDictionary) {
            if (!StyledTextLineOps.HasVisibleText(indicator)) {
                return (string.Empty, []);
            }

            var text = string.Concat((indicator.Runs ?? [])
                .Where(static run => !string.IsNullOrEmpty(run.Text))
                .Select(static run => run.Text));
            if (string.IsNullOrEmpty(text)) {
                return (string.Empty, []);
            }

            var styleSlots = TerminalTextLayoutOps.BuildStatusStyleSlots(indicator.Runs ?? [], styleDictionary);
            return styleSlots.Any(static slot => slot != 0)
                ? (text, styleSlots)
                : (text, []);
        }

        private static DisplayComposition BuildDisplayComposition(
            LineEditorRenderState state,
            TerminalSurfaceRuntimeFrame frame,
            InlineSegments editorContent) {
            var text = state.Text ?? string.Empty;
            var cursorIndex = Math.Clamp(state.CursorIndex, 0, text.Length);
            var hasSynchronizedContent = string.Equals(editorContent.Text ?? string.Empty, text, StringComparison.Ordinal);
            IReadOnlyList<InlineTextInsertion> ghostInsertions = hasSynchronizedContent && state.OverlayPlan.GhostControl is not null
                ? TerminalProjectionInputAdapter.ResolveGhostHint(frame).Insertions ?? []
                : [];
            var contentStyleSlots = BuildContentStyleSlots(text, editorContent, frame.StyleDictionary);
            var markers = EditorTextMarkerOps.Normalize(state.Markers, text.Length);

            if (ghostInsertions.Count == 0 && markers.Length == 0) {
                return new DisplayComposition(
                    text,
                    contentStyleSlots,
                    cursorIndex);
            }

            StringBuilder builder = new();
            List<ushort> styleSlots = [];
            var cursorCompositeIndex = -1;

            for (var sourceIndex = 0; sourceIndex <= text.Length; sourceIndex++) {
                if (sourceIndex == cursorIndex) {
                    cursorCompositeIndex = builder.Length;
                }

                foreach (var insertion in ghostInsertions.Where(insertion =>
                    Math.Clamp(insertion.SourceIndex, 0, text.Length) == sourceIndex
                    && !string.IsNullOrEmpty(insertion.Content.Text))) {
                    builder.Append(insertion.Content.Text);
                    var insertionStyleSlots = BuildContentStyleSlots(
                        insertion.Content.Text,
                        insertion.Content,
                        frame.StyleDictionary,
                        TerminalTextLayoutOps.ResolveStyleSlot(frame.StyleDictionary, SurfaceStyleCatalog.InlineHint));
                    for (var index = 0; index < insertion.Content.Text.Length; index++) {
                        styleSlots.Add(insertionStyleSlots[index]);
                    }
                }

                if (sourceIndex >= text.Length) {
                    continue;
                }

                if (TryResolveMarker(markers, sourceIndex, out var marker)) {
                    var markerText = ResolveMarkerDisplayText(marker, state.MarkerCatalog, text);
                    var markerStyleSlot = ResolveMarkerStyleSlot(marker, state.MarkerCatalog, contentStyleSlots);
                    builder.Append(markerText);
                    for (var index = 0; index < markerText.Length; index++) {
                        styleSlots.Add(markerStyleSlot);
                    }

                    if (marker.StartIndex + marker.Length == cursorIndex) {
                        cursorCompositeIndex = builder.Length;
                    }

                    sourceIndex = marker.StartIndex + marker.Length - 1;
                    continue;
                }

                builder.Append(text[sourceIndex]);
                styleSlots.Add(contentStyleSlots[sourceIndex]);
            }

            if (cursorCompositeIndex < 0) {
                cursorCompositeIndex = builder.Length;
            }

            return new DisplayComposition(
                builder.ToString(),
                [.. styleSlots],
                cursorCompositeIndex);
        }

        private static StatusFrame BuildStatusFrame(
            TerminalSurfaceRuntimeFrame frame,
            TerminalOverlay overlay,
            TerminalOverlayLayoutPlan overlayPlan,
            TerminalStatusBarState? statusBar,
            int requestedScrollOffset) {
            var styleDictionary = statusBar?.Frame.StyleDictionary ?? new StyleDictionary();
            List<StatusRenderLine> pinnedDetailLines = GetPinnedStatusDetailLines(statusBar)
                .Where(StyledTextLineOps.HasVisibleText)
                .Take(MaxStatusBodyLines)
                .Select(StatusRenderLine.FromStyled)
                .ToList();
            var remainingBodySlots = Math.Max(0, MaxStatusBodyLines - pinnedDetailLines.Count);
            List<StatusRenderLine> contentLines = BuildStatusContentLines(frame, overlay, overlayPlan);
            var totalBodyLines = contentLines.Count;
            var maxScroll = remainingBodySlots <= 0
                ? 0
                : Math.Max(0, totalBodyLines - remainingBodySlots);
            var scrollOffset = Math.Clamp(requestedScrollOffset, 0, maxScroll);
            List<StatusRenderLine> visibleBodyLines = remainingBodySlots <= 0
                ? []
                : [.. contentLines.Skip(scrollOffset).Take(remainingBodySlots)];

            var startLine = totalBodyLines == 0 ? 0 : scrollOffset + 1;
            var endLine = totalBodyLines == 0 ? 0 : scrollOffset + visibleBodyLines.Count;
            var headerLine = FormatStatusHeaderLine(
                frame,
                overlay,
                overlayPlan,
                statusBar,
                startLine,
                endLine,
                totalBodyLines);
            List<StatusRenderLine> lines = [];
            if (HasVisibleStatusText(headerLine)) {
                lines.Add(headerLine);
            }

            lines.AddRange(pinnedDetailLines);
            lines.AddRange(visibleBodyLines);
            return new StatusFrame(lines, styleDictionary, scrollOffset);
        }

        private static StyledTextLine[] GetPinnedStatusDetailLines(TerminalStatusBarState? statusBar) {
            return statusBar is null
                ? []
                : TerminalProjectionStatusAdapter.ResolveSessionDetailLines(statusBar.Frame);
        }

        private static StatusRenderLine FormatStatusHeaderLine(
            TerminalSurfaceRuntimeFrame frame,
            TerminalOverlay overlay,
            TerminalOverlayLayoutPlan overlayPlan,
            TerminalStatusBarState? statusBarSurface,
            int startLine,
            int endLine,
            int totalBodyLines) {
            var interactionLine = BuildInteractionStatusLine(frame, overlay, overlayPlan, statusBarSurface);
            var hasInteractionLine = StyledTextLineOps.HasVisibleText(interactionLine);
            var hasStatusBar = statusBarSurface is not null;
            StyledTextLineBuilder line = new();

            if (!hasStatusBar) {
                if (hasInteractionLine) {
                    line.Append(interactionLine);
                }
            }
            if (hasStatusBar) {
                var hasLeadingSegment = false;
                var hasDelimitedSegment = false;
                var statusTitle = TerminalProjectionStatusAdapter.ResolveSessionTitleLine(statusBarSurface!.Frame);
                if (StyledTextLineOps.HasVisibleText(statusTitle)) {
                    line.Append(statusTitle);
                    hasLeadingSegment = true;
                }

                var indicator = statusBarSurface.Indicator.RenderedFrame;
                if (StyledTextLineOps.HasVisibleText(indicator)) {
                    if (hasLeadingSegment) {
                        line.Append(" ");
                    }

                    line.Append(indicator);
                    hasLeadingSegment = true;
                }

                var compactLine = TerminalProjectionStatusAdapter.ResolveSessionCompactLine(statusBarSurface.Frame);
                if (StyledTextLineOps.HasVisibleText(compactLine)) {
                    if (hasLeadingSegment) {
                        line.Append(" ");
                    }

                    line.Append(compactLine);
                    hasLeadingSegment = true;
                    hasDelimitedSegment = true;
                }

                var headerLine = TerminalProjectionStatusAdapter.ResolveSessionHeaderLine(statusBarSurface.Frame);
                if (StyledTextLineOps.HasVisibleText(headerLine)) {
                    if (hasDelimitedSegment) {
                        line.Append(" | ");
                    }
                    else if (hasLeadingSegment) {
                        line.Append(" ");
                    }

                    line.Append(headerLine);
                    hasLeadingSegment = true;
                    hasDelimitedSegment = true;
                }

                if (hasInteractionLine) {
                    if (hasDelimitedSegment) {
                        line.Append(" | ");
                    }
                    else if (hasLeadingSegment) {
                        line.Append(" ");
                    }

                    line.Append(interactionLine);
                }
            }

            if (totalBodyLines > 0) {
                line.Append($"[{startLine}~{endLine}/{totalBodyLines}]");
            }
            var lineStyleId = ResolveHeaderLineStyleId(
                statusBarSurface is null ? null : TerminalProjectionStatusAdapter.ResolveSessionTitleLine(statusBarSurface.Frame),
                statusBarSurface is null ? null : TerminalProjectionStatusAdapter.ResolveSessionCompactLine(statusBarSurface.Frame),
                statusBarSurface is null ? null : TerminalProjectionStatusAdapter.ResolveSessionHeaderLine(statusBarSurface.Frame),
                interactionLine);
            return StatusRenderLine.FromStyled(new StyledTextLine {
                LineStyleId = lineStyleId,
                Runs = line.Build().Runs,
            });
        }

        private static List<StatusRenderLine> BuildStatusContentLines(
            TerminalSurfaceRuntimeFrame frame,
            TerminalOverlay overlay,
            TerminalOverlayLayoutPlan overlayPlan) {
            List<StatusRenderLine> lines = [.. BuildStatusBodyLines(frame, overlayPlan)
                .Select(StatusRenderLine.FromStyled)];
            lines.AddRange(BuildStatusInterpretationLines(frame, overlay, overlayPlan));
            return lines;
        }

        private static List<StyledTextLine> BuildStatusBodyLines(
            TerminalSurfaceRuntimeFrame frame,
            TerminalOverlayLayoutPlan overlayPlan) {
            if (overlayPlan.StatusBodyControl is null) {
                return [];
            }

            return [.. GetStatusLines(frame, SurfaceStatusFieldKeys.Detail)
                .Where(StyledTextLineOps.HasVisibleText)];
        }

        private static List<StatusRenderLine> BuildStatusInterpretationLines(
            TerminalSurfaceRuntimeFrame frame,
            TerminalOverlay overlay,
            TerminalOverlayLayoutPlan overlayPlan) {
            var control = overlayPlan.InterpretationStatusControl;
            if (control is null) {
                return [];
            }

            return BuildStatusInterpretationRows(
                frame,
                overlay.FindTextTemplate(control));
        }

        private static List<StatusRenderLine> BuildStatusInterpretationRows(
            TerminalSurfaceRuntimeFrame frame,
            TextTemplate? interpretationTemplate) {
            var interpretation = TerminalProjectionAssistAdapter.FindInterpretation(frame);
            var interpretationSummary = ProjectionStyledTextAdapter.ToInlineSegments(
                TerminalProjectionAssistAdapter.FindInterpretationSummary(frame)?.State.Content);
            var interpretationDetail = TerminalProjectionAssistAdapter.FindInterpretationDetail(frame);
            var detailLines = ProjectionStyledTextAdapter.ToInlineSegments(interpretationDetail?.State.Lines);
            var options = interpretation?.State.Items ?? [];
            var activeInterpretationIndex = TerminalProjectionAssistAdapter.ResolveSelectedItemIndex(
                frame,
                interpretation,
                interpretationDetail);
            var activeLabel = options.Length > 1 && activeInterpretationIndex >= 0 && activeInterpretationIndex < options.Length
                ? ProjectionStyledTextAdapter.ToInlineSegments(options[activeInterpretationIndex].Label)
                : new InlineSegments();
            var hasHint = !string.IsNullOrWhiteSpace(interpretationSummary.Text)
                || detailLines.Any(InlineSegmentsOps.HasVisibleText)
                || options.Length > 1;
            if (!hasHint) {
                return [];
            }

            const string lineStyleId = SurfaceStyleCatalog.StatusDetail;
            var maxInterpretationLines = interpretationTemplate is { MaxLines: > 0 }
                ? interpretationTemplate.MaxLines
                : int.MaxValue;
            List<StatusRenderLine> lines = [];
            var firstDetailLineIndex = 0;
            var summaryRenderedInline = false;
            if (TryBuildIndexedStatusInterpretationLine(detailLines, activeInterpretationIndex, options.Length, out var indexedDetailLine)) {
                lines.Add(CreateStyledStatusLine(indexedDetailLine, lineStyleId));
                firstDetailLineIndex = 1;
                summaryRenderedInline = true;
            }
            else if (TryBuildStatusInterpretationHeaderLine(
                interpretationSummary,
                activeLabel,
                activeInterpretationIndex,
                options.Length,
                out var headerLine,
                out summaryRenderedInline)) {
                lines.Add(CreateStyledStatusLine(headerLine, lineStyleId));
            }

            if (!summaryRenderedInline
                && lines.Count < maxInterpretationLines
                && InlineSegmentsOps.HasVisibleText(interpretationSummary)) {
                lines.Add(CreateStyledStatusLine(interpretationSummary, lineStyleId));
            }

            for (var detailIndex = firstDetailLineIndex; detailIndex < detailLines.Length; detailIndex++) {
                var detailLine = detailLines[detailIndex];
                if (lines.Count >= maxInterpretationLines) {
                    break;
                }

                if (!InlineSegmentsOps.HasVisibleText(detailLine)) {
                    continue;
                }

                lines.Add(CreateStyledStatusLine(detailLine, lineStyleId));
            }

            return lines;
        }

        private static bool TryBuildIndexedStatusInterpretationLine(
            InlineSegments[] detailLines,
            int activeInterpretationIndex,
            int optionCount,
            out InlineSegments line) {
            line = new InlineSegments();
            if (optionCount <= 1
                || activeInterpretationIndex < 0
                || detailLines.Length == 0
                || !InlineSegmentsOps.HasVisibleText(detailLines[0])) {
                return false;
            }

            var source = detailLines[0];
            var separatorIndex = source.Text.IndexOf(':');
            if (separatorIndex <= 0) {
                return false;
            }

            var indexedPrefix = source.Text[..separatorIndex] + $"[{activeInterpretationIndex + 1}/{optionCount}]";
            var insertedLength = indexedPrefix.Length - separatorIndex;
            line = new InlineSegments {
                Text = indexedPrefix + source.Text[separatorIndex..],
                Highlights = [.. (source.Highlights ?? [])
                    .Select(highlight => new HighlightSpan {
                        StartIndex = highlight.StartIndex >= separatorIndex
                            ? highlight.StartIndex + insertedLength
                            : highlight.StartIndex,
                        Length = highlight.Length,
                        StyleId = highlight.StyleId,
                    })],
            };
            return true;
        }

        private static StyledTextLine BuildInteractionStatusLine(
            TerminalSurfaceRuntimeFrame frame,
            TerminalOverlay overlay,
            TerminalOverlayLayoutPlan overlayPlan,
            TerminalStatusBarState? statusBarSurface) {
            var control = statusBarSurface is null
                ? overlayPlan.StatusHeaderControl
                : overlayPlan.StatusSummaryControl;
            return TerminalStatusFormatter.FormatFirstRow(
                fieldKey => TerminalProjectionStatusAdapter.ResolveInteractionFieldLines(frame, fieldKey),
                overlay.FindStatusBarTemplate(control),
                compact: false);
        }

        private static StyledTextLine[] GetStatusLines(TerminalSurfaceRuntimeFrame frame, string fieldKey) {
            return TerminalProjectionStatusAdapter.ResolveInteractionFieldLines(frame, fieldKey);
        }

        private static bool HasVisibleStatusText(StatusRenderLine line) {
            return line.StyledText is StyledTextLine styledText
                ? StyledTextLineOps.HasVisibleText(styledText)
                : !string.IsNullOrWhiteSpace(line.Text);
        }

        private static List<StatusRenderLine> BuildInlineAssistOverlayLines(
            TerminalSurfaceRuntimeFrame frame,
            LineEditorRenderState inputState,
            int writableWidth) {
            var assistRoot = inputState.OverlayPlan.InlineAssistRoot;
            if (assistRoot is null) {
                return [];
            }

            return BuildInlineAssistRows(frame, inputState, assistRoot, writableWidth);
        }

        private static PanePopupMaterial? BuildPopupAssistMaterial(
            TerminalSurfaceRuntimeFrame frame,
            LineEditorRenderState inputState,
            int writableWidth) {
            var assistRoot = inputState.OverlayPlan.PopupAssistRoot;
            if (assistRoot is null) {
                return null;
            }

            var horizontalViewportMargin = ResolvePopupAssistHorizontalViewportMargin(
                assistRoot.MinHorizontalViewportMargin,
                writableWidth);
            var maxOuterWidth = Math.Max(3, writableWidth - (horizontalViewportMargin * 2));
            var maxInnerWidth = Math.Max(1, maxOuterWidth - 2);
            var maxPaddedContentWidth = Math.Max(1, maxInnerWidth - (PopupHorizontalPadding * 2));
            PopupCompletionPanel? completionPanel = null;
            PopupContentLine[] interpretationLines = [];
            var interpretationInnerWidth = 0;
            CollectPopupAssistMaterial(
                assistRoot,
                inputState.Overlay,
                frame,
                inputState,
                maxInnerWidth,
                maxPaddedContentWidth,
                ref completionPanel,
                ref interpretationLines,
                ref interpretationInnerWidth);

            if (interpretationLines.Length > 0) {
                var popupTextSlot = TerminalTextLayoutOps.ResolveStyleSlot(frame.StyleDictionary, SurfaceStyleCatalog.CompletionPopupText);
                var popupDetailSlot = TerminalTextLayoutOps.ResolveStyleSlot(frame.StyleDictionary, SurfaceStyleCatalog.CompletionPopupDetail);
                interpretationLines = [.. interpretationLines.Select(line => AddPopupHorizontalPadding(
                    TerminalTextLayoutOps.PadPopupContentLine(
                        ApplyPopupContentBackground(line, popupDetailSlot),
                        interpretationInnerWidth,
                        paddingBackgroundStyleSlot: popupDetailSlot),
                    PopupHorizontalPadding,
                    PopupHorizontalPadding,
                    popupTextSlot))];
                interpretationInnerWidth += PopupHorizontalPadding * 2;
            }

            if (completionPanel is null && interpretationLines.Length == 0) {
                return null;
            }

            return new PanePopupMaterial(
                completionPanel,
                interpretationLines,
                interpretationInnerWidth,
                TerminalTextLayoutOps.ResolveStyleSlot(frame.StyleDictionary, SurfaceStyleCatalog.CompletionPopupBorder),
                horizontalViewportMargin);
        }

        private static void CollectPopupAssistMaterial(
            OverlayControlDefinition control,
            TerminalOverlay overlay,
            TerminalSurfaceRuntimeFrame frame,
            LineEditorRenderState inputState,
            int maxInnerWidth,
            int maxPaddedContentWidth,
            ref PopupCompletionPanel? completionPanel,
            ref PopupContentLine[] interpretationLines,
            ref int interpretationInnerWidth) {
            switch (control.Kind) {
                case OverlayControlKind.Stack:
                    foreach (var child in control.Children ?? []) {
                        CollectPopupAssistMaterial(
                            child,
                            overlay,
                            frame,
                            inputState,
                            maxInnerWidth,
                            maxPaddedContentWidth,
                            ref completionPanel,
                            ref interpretationLines,
                            ref interpretationInnerWidth);
                    }
                    break;
                case OverlayControlKind.List:
                    if (completionPanel is null
                        && control.AnchorTarget == OverlayAnchorTarget.Caret
                        && ControlsMatch(control, inputState.OverlayPlan.CompletionControl)) {
                        completionPanel = BuildPopupCompletionPanel(
                            frame,
                            inputState,
                            maxInnerWidth,
                            overlay.FindListTemplate(control));
                    }
                    break;
                case OverlayControlKind.Text:
                    if (interpretationLines.Length == 0
                        && control.AnchorTarget == OverlayAnchorTarget.Caret
                        && ControlsMatch(control, inputState.OverlayPlan.InterpretationAssistControl)) {
                        interpretationLines = BuildPopupAssistInterpretationLines(
                            frame,
                            inputState.InterpretationDismissed,
                            overlay.FindTextTemplate(control),
                            maxPaddedContentWidth,
                            out interpretationInnerWidth);
                    }
                    break;
            }
        }

        private static int ResolvePopupAssistHorizontalViewportMargin(int requestedMargin, int writableWidth) {
            return Math.Clamp(
                Math.Max(0, requestedMargin),
                0,
                Math.Max(0, (Math.Max(0, writableWidth) - 3) / 2));
        }

        private static PopupContentLine[] BuildPopupAssistInterpretationLines(
            TerminalSurfaceRuntimeFrame frame,
            bool interpretationDismissed,
            TextTemplate? interpretationTemplate,
            int maxInnerWidth,
            out int interpretationInnerWidth) {
            var interpretation = TerminalProjectionAssistAdapter.FindInterpretation(frame);
            var interpretationSummary = TerminalProjectionAssistAdapter.FindInterpretationSummary(frame);
            var interpretationDetail = TerminalProjectionAssistAdapter.FindInterpretationDetail(frame);
            var activeInterpretationIndex = TerminalProjectionAssistAdapter.ResolveSelectedItemIndex(
                frame,
                interpretation,
                interpretationDetail);
            var optionCount = interpretation?.State.Items.Length ?? 0;
            if (TerminalProjectionAssistAdapter.UsesExpandedInterpretationPopupLayout(frame)) {
                return BuildExpandedPopupInterpretationLines(
                    interpretationSummary,
                    interpretationDetail,
                    activeInterpretationIndex,
                    optionCount,
                    interpretationDismissed,
                    maxInnerWidth,
                    Math.Max(1, interpretationTemplate?.MaxLines ?? 6),
                    frame.StyleDictionary,
                    out interpretationInnerWidth);
            }

            PopupContentLine[] interpretationLines = [.. BuildPopupInterpretationLines(frame, interpretationDismissed)];
            if (interpretationTemplate is { MaxLines: > 0 } && interpretationLines.Length > interpretationTemplate.MaxLines) {
                interpretationLines = [.. interpretationLines.Take(interpretationTemplate.MaxLines)];
            }
            if (interpretationLines.Length > 0) {
                interpretationLines = WrapPopupInterpretationLines(interpretationLines, maxInnerWidth);
            }

            interpretationInnerWidth = interpretationLines.Length == 0
                ? 0
                : Math.Clamp(
                    Math.Max(8, interpretationLines.Max(static line => TerminalCellWidth.Measure(line.Text))),
                    1,
                    maxInnerWidth);
            return interpretationLines;
        }

        private static PopupCompletionPanel? BuildPopupCompletionPanel(
            TerminalSurfaceRuntimeFrame frame,
            LineEditorRenderState inputState,
            int maxInnerWidth,
            ListTemplate? completionTemplate) {
            var completion = TerminalProjectionAssistAdapter.FindCompletion(frame);
            ProjectionCollectionItem[] items = completion?.State.Items ?? [];
            if (inputState.CompletionCount <= 0 || items.Length == 0) {
                return null;
            }

            var selectedLocalIndex = completion is { State.IsPaged: true }
                ? inputState.CompletionIndex - Math.Max(0, completion.Value.State.WindowOffset) - 1
                : inputState.CompletionIndex - 1;
            if (selectedLocalIndex < 0 || selectedLocalIndex >= items.Length) {
                selectedLocalIndex = 0;
            }

            var maxRows = Math.Min(
                completionTemplate is { MaxVisibleItems: > 0 } ? completionTemplate.MaxVisibleItems : PopupCompletionMaxRows,
                items.Length);
            var start = Math.Clamp(selectedLocalIndex - (maxRows / 2), 0, Math.Max(0, items.Length - maxRows));
            var selectedWindowIndex = selectedLocalIndex - start;
            List<PopupCompletionRow> rows = [];
            var labelWidth = 0;
            for (var i = 0; i < maxRows; i++) {
                var itemIndex = start + i;
                ProjectionCollectionItem item = items[itemIndex];
                StyledTextRun[] labelRuns = ResolvePopupCompletionRuns(item.Label, item.ItemId);
                StyledTextRun[] summaryRuns = ResolvePopupCompletionRuns(item.Summary);
                rows.Add(new PopupCompletionRow(
                    item,
                    itemIndex == selectedLocalIndex,
                    labelRuns,
                    summaryRuns));
                labelWidth = Math.Max(labelWidth, TerminalTextLayoutOps.MeasureStyledTextRuns(labelRuns));
            }

            labelWidth = Math.Clamp(labelWidth, 4, 28);
            ProjectionCollectionItem selectedItem = rows[selectedWindowIndex].Item;
            var listInnerWidth = 1 + labelWidth + PopupHorizontalPadding;
            PopupContentLine[] detailLines = [];
            var detailInnerWidth = 0;
            if (completionTemplate?.DetailPlacement == OverlayListDetailPlacement.RightPopout
                && maxInnerWidth >= listInnerWidth + 16) {
                var availableSummaryWidth = Math.Max(12, maxInnerWidth - listInnerWidth - 3);
                var wrapSummaryWidth = Math.Clamp(availableSummaryWidth, 12, 72);
                detailLines = BuildPopupDetailPaneLines(selectedItem, wrapSummaryWidth, maxLines: 3, frame.StyleDictionary);
                detailInnerWidth = detailLines.Length == 0
                    ? 0
                    : Math.Max(12, detailLines.Max(static line => TerminalCellWidth.Measure(line.Text)));
                if (detailInnerWidth > 0) {
                    var popupTextSlot = TerminalTextLayoutOps.ResolveStyleSlot(frame.StyleDictionary, SurfaceStyleCatalog.CompletionPopupText);
                    var selectedSlot = TerminalTextLayoutOps.ResolveStyleSlot(frame.StyleDictionary, SurfaceStyleCatalog.CompletionItemSelected);
                    detailLines = [.. detailLines.Select(line => AddPopupHorizontalPadding(
                        TerminalTextLayoutOps.PadPopupContentLine(
                            TerminalTextLayoutOps.FitPopupContentLine(line, detailInnerWidth),
                            detailInnerWidth,
                            popupTextSlot,
                            selectedSlot),
                        PopupHorizontalPadding,
                        PopupHorizontalPadding,
                        popupTextSlot))];
                    detailInnerWidth += PopupHorizontalPadding * 2;
                }
            }

            List<PopupContentLine> listLines = [];
            for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++) {
                PopupCompletionRow row = rows[rowIndex];
                StyledTextRun[] fittedLabel = TerminalTextLayoutOps.FitStyledTextRuns(new StyledTextLine { Runs = row.LabelRuns }, labelWidth);
                var labelText = string.Concat(fittedLabel.Select(static run => run.Text));
                var labelStyles = TerminalTextLayoutOps.NormalizePopupStyleSlots(labelText, TerminalTextLayoutOps.BuildStatusStyleSlots(fittedLabel, frame.StyleDictionary));
                if (TerminalCellWidth.Measure(labelText) < labelWidth) {
                    var popupTextSlot = TerminalTextLayoutOps.ResolveStyleSlot(frame.StyleDictionary, SurfaceStyleCatalog.CompletionPopupText);
                    var padded = TerminalTextLayoutOps.PadPopupContentLine(new PopupContentLine(labelText, labelStyles, []), labelWidth, popupTextSlot);
                    labelText = padded.Text;
                    labelStyles = padded.StyleSlots;
                }

                var marker = row.Selected ? ">" : " ";
                var leftText = marker + labelText + new string(' ', PopupHorizontalPadding);
                var leftStyleSlots = Enumerable.Repeat(TerminalTextLayoutOps.ResolveStyleSlot(frame.StyleDictionary, SurfaceStyleCatalog.CompletionPopupText), leftText.Length).ToArray();
                var leftBackgroundStyleSlots = new ushort[leftText.Length];
                if (labelStyles.Length > 0) {
                    Array.Copy(labelStyles, 0, leftStyleSlots, 1, Math.Min(labelStyles.Length, leftStyleSlots.Length - 1));
                }

                if (row.Selected) {
                    leftStyleSlots[0] = TerminalTextLayoutOps.ResolveStyleSlot(frame.StyleDictionary, SurfaceStyleCatalog.CompletionItemSelectedMarker);
                    var selectedLength = labelText.Length;
                    var selectedSlot = TerminalTextLayoutOps.ResolveStyleSlot(frame.StyleDictionary, SurfaceStyleCatalog.CompletionItemSelected);
                    for (var index = 1; index <= selectedLength && index < leftStyleSlots.Length; index++) {
                        leftBackgroundStyleSlots[index] = selectedSlot;
                    }
                }

                listLines.Add(new PopupContentLine(leftText, [.. leftStyleSlots], [.. leftBackgroundStyleSlots]));
            }

            return new PopupCompletionPanel([.. listLines], listInnerWidth, detailLines, detailInnerWidth, selectedWindowIndex);
        }

        private static PopupContentLine[] BuildPopupDetailPaneLines(
            ProjectionCollectionItem item,
            int summaryWidth,
            int maxLines,
            StyleDictionary styleDictionary) {
            if (summaryWidth <= 0 || maxLines <= 0) {
                return [];
            }

            var logicalLines = BuildPopupDetailLogicalLines(item, styleDictionary);
            if (logicalLines.Count == 0) {
                return [];
            }

            List<PopupContentLine> lines = [];
            var selectedSlot = TerminalTextLayoutOps.ResolveStyleSlot(styleDictionary, SurfaceStyleCatalog.CompletionItemSelected);
            foreach (PopupStyledLine logicalLine in logicalLines) {
                foreach (PopupContentLine wrappedLine in WrapPopupStyledLine(logicalLine, summaryWidth, maxLines - lines.Count, selectedSlot, styleDictionary)) {
                    lines.Add(wrappedLine);
                    if (lines.Count >= maxLines) {
                        return [.. lines];
                    }
                }
            }

            return [.. lines];
        }

        private static PopupContentLine[] BuildExpandedPopupInterpretationLines(
            ProjectionTextNodeRuntime? interpretationSummary,
            ProjectionDetailNodeRuntime? interpretationDetail,
            int activeInterpretationIndex,
            int optionCount,
            bool interpretationDismissed,
            int maxInnerWidth,
            int maxLines,
            StyleDictionary styleDictionary,
            out int innerWidth) {
            innerWidth = 0;
            StyledTextLine[] summaryLines = ProjectionStyledTextAdapter.ToStyledLines(interpretationSummary?.State.Content);
            StyledTextLine[] detailLines = ProjectionStyledTextAdapter.ToStyledLines(interpretationDetail?.State.Lines);
            var hasHint = !interpretationDismissed
                && (summaryLines.Any(StyledTextLineOps.HasVisibleText)
                    || detailLines.Any(StyledTextLineOps.HasVisibleText)
                    || optionCount > 1);
            if (!hasHint || maxInnerWidth <= 0 || maxLines <= 0) {
                return [];
            }

            var counter = optionCount > 1 && activeInterpretationIndex >= 0
                ? $"{activeInterpretationIndex + 1}/{optionCount} "
                : string.Empty;
            var continuationPrefix = new string(' ', counter.Length);
            var contentWidth = Math.Max(1, maxInnerWidth - TerminalCellWidth.Measure(counter));
            List<PopupContentLine> lines = [];
            var firstRenderedLine = true;
            AppendPopupInterpretationContentLines(lines, summaryLines, counter, continuationPrefix, contentWidth, maxLines, styleDictionary, ref firstRenderedLine);
            AppendPopupInterpretationContentLines(lines, detailLines, counter, continuationPrefix, contentWidth, maxLines, styleDictionary, ref firstRenderedLine);
            if (lines.Count == 0) {
                return [];
            }

            innerWidth = Math.Clamp(
                Math.Max(8, lines.Max(static line => TerminalCellWidth.Measure(line.Text))),
                1,
                maxInnerWidth);
            return [.. lines];
        }

        private static void AppendPopupInterpretationContentLines(
            List<PopupContentLine> lines,
            IReadOnlyList<StyledTextLine> contentLines,
            string firstPrefix,
            string continuationPrefix,
            int maxContentWidth,
            int maxLines,
            StyleDictionary styleDictionary,
            ref bool firstRenderedLine) {
            if (maxContentWidth <= 0 || lines.Count >= maxLines) {
                return;
            }

            foreach (var contentLine in contentLines) {
                if (!StyledTextLineOps.HasVisibleText(contentLine)) {
                    continue;
                }

                PopupContentLine logicalLine = BuildPopupContentLine(contentLine, styleDictionary);
                PopupContentLine[] wrappedLines = WrapPopupInterpretationLine(logicalLine, maxContentWidth);
                foreach (var wrappedLine in wrappedLines) {
                    if (lines.Count >= maxLines) {
                        return;
                    }

                    var prefix = firstRenderedLine ? firstPrefix : continuationPrefix;
                    lines.Add(PrependPopupPrefix(wrappedLine, prefix, styleDictionary));
                    firstRenderedLine = false;
                }
            }
        }

        private static List<PopupContentLine> BuildPopupInterpretationLines(TerminalSurfaceRuntimeFrame frame, bool interpretationDismissed) {
            var interpretation = TerminalProjectionAssistAdapter.FindInterpretation(frame);
            var interpretationSummary = ProjectionStyledTextAdapter.ToInlineSegments(
                TerminalProjectionAssistAdapter.FindInterpretationSummary(frame)?.State.Content);
            var interpretationDetail = TerminalProjectionAssistAdapter.FindInterpretationDetail(frame);
            var detailLines = ProjectionStyledTextAdapter.ToInlineSegments(interpretationDetail?.State.Lines);
            var options = interpretation?.State.Items ?? [];
            var activeInterpretationIndex = TerminalProjectionAssistAdapter.ResolveSelectedItemIndex(
                frame,
                interpretation,
                interpretationDetail);
            var activeLabel = options.Length > 1 && activeInterpretationIndex >= 0 && activeInterpretationIndex < options.Length
                ? ProjectionStyledTextAdapter.ToInlineSegments(options[activeInterpretationIndex].Label)
                : new InlineSegments();
            var hasHint = !interpretationDismissed
                && (!string.IsNullOrWhiteSpace(interpretationSummary.Text)
                    || detailLines.Any(InlineSegmentsOps.HasVisibleText)
                    || options.Length > 1);
            if (!hasHint) {
                return [];
            }

            List<PopupContentLine> lines = [];
            var summaryUsed = false;
            var labelUsed = false;
            var usedDetailIndex = -1;

            if (TryBuildPopupInterpretationHeaderLine(
                interpretationSummary,
                activeLabel,
                detailLines,
                activeInterpretationIndex,
                options.Length,
                out PopupContentLine headerLine,
                out summaryUsed,
                out labelUsed,
                out usedDetailIndex,
                frame.StyleDictionary)) {
                lines.Add(headerLine);
            }

            if (!labelUsed && ShouldRenderPopupInterpretationLabel(activeLabel, options.Length)) {
                lines.Add(BuildPopupInterpretationContentLine("  ", activeLabel, frame.StyleDictionary));
            }

            if (!summaryUsed && InlineSegmentsOps.HasVisibleText(interpretationSummary)) {
                lines.Add(BuildPopupInterpretationContentLine("  ", interpretationSummary, frame.StyleDictionary));
            }

            var emitted = 0;
            for (var index = 0; index < detailLines.Length; index++) {
                if (index == usedDetailIndex) {
                    continue;
                }

                InlineSegments detailLine = detailLines[index];
                if (!InlineSegmentsOps.HasVisibleText(detailLine)) {
                    continue;
                }

                lines.Add(BuildPopupInterpretationContentLine("  ", detailLine, frame.StyleDictionary));
                emitted += 1;
                if (emitted >= 2) {
                    break;
                }
            }

            return lines;
        }

        private static bool TryBuildPopupInterpretationHeaderLine(
            InlineSegments summary,
            InlineSegments activeLabel,
            InlineSegments[] detailLines,
            int activeInterpretationIndex,
            int optionCount,
            out PopupContentLine line,
            out bool usedSummary,
            out bool usedLabel,
            out int usedDetailIndex,
            StyleDictionary styleDictionary) {
            usedSummary = false;
            usedLabel = false;
            usedDetailIndex = -1;

            var counter = optionCount > 1 && activeInterpretationIndex >= 0
                ? $"{activeInterpretationIndex + 1}/{optionCount}"
                : string.Empty;
            InlineSegments primary = new();
            if (InlineSegmentsOps.HasVisibleText(summary)) {
                primary = summary;
                usedSummary = true;
            }
            else if (ShouldRenderPopupInterpretationLabel(activeLabel, optionCount)) {
                primary = activeLabel;
                usedLabel = true;
            }
            else if (TryGetPopupInterpretationDetailLine(detailLines, out var detailIndex, out var detailLine)) {
                primary = detailLine;
                usedDetailIndex = detailIndex;
            }

            if (string.IsNullOrWhiteSpace(counter) && !InlineSegmentsOps.HasVisibleText(primary)) {
                line = default;
                return false;
            }

            StyledTextLineBuilder builder = new();
            if (!string.IsNullOrWhiteSpace(counter)) {
                builder.Append(counter, SurfaceStyleCatalog.InlineHint);
                if (InlineSegmentsOps.HasVisibleText(primary)) {
                    builder.Append("  ");
                }
            }

            if (InlineSegmentsOps.HasVisibleText(primary)) {
                builder.Append(InlineSegmentsOps.ToStyledTextLine(primary));
            }

            line = BuildPopupContentLine(builder.Build(), styleDictionary);
            return true;
        }

        private static bool TryGetPopupInterpretationDetailLine(
            InlineSegments[] detailLines,
            out int index,
            out InlineSegments line) {
            for (index = 0; index < detailLines.Length; index++) {
                if (!InlineSegmentsOps.HasVisibleText(detailLines[index])) {
                    continue;
                }

                line = detailLines[index];
                return true;
            }

            index = -1;
            line = new();
            return false;
        }

        private static bool ShouldRenderPopupInterpretationLabel(InlineSegments label, int optionCount) {
            return InlineSegmentsOps.HasVisibleText(label);
        }

        private static PopupContentLine BuildPopupInterpretationContentLine(string prefix, InlineSegments content, StyleDictionary styleDictionary) {
            StyledTextLineBuilder builder = new();
            if (!string.IsNullOrEmpty(prefix)) {
                builder.Append(prefix);
            }

            builder.Append(InlineSegmentsOps.ToStyledTextLine(content));
            return BuildPopupContentLine(builder.Build(), styleDictionary);
        }

        private static PopupContentLine PrependPopupPrefix(PopupContentLine line, string prefix, StyleDictionary styleDictionary) {
            if (string.IsNullOrEmpty(prefix)) {
                return line;
            }

            var prefixStyles = Enumerable.Repeat(TerminalTextLayoutOps.ResolveStyleSlot(styleDictionary, SurfaceStyleCatalog.CompletionPopupText), prefix.Length).ToArray();
            var prefixBackgrounds = new ushort[prefix.Length];
            var styleSlots = new ushort[prefix.Length + line.StyleSlots.Length];
            var backgroundStyleSlots = new ushort[prefix.Length + line.BackgroundStyleSlots.Length];
            Array.Copy(prefixStyles, styleSlots, prefixStyles.Length);
            Array.Copy(prefixBackgrounds, backgroundStyleSlots, prefixBackgrounds.Length);
            Array.Copy(line.StyleSlots, 0, styleSlots, prefix.Length, line.StyleSlots.Length);
            Array.Copy(line.BackgroundStyleSlots, 0, backgroundStyleSlots, prefix.Length, line.BackgroundStyleSlots.Length);
            return new PopupContentLine(prefix + line.Text, styleSlots, backgroundStyleSlots);
        }

        private static PopupContentLine BuildPopupContentLine(StyledTextLine line, StyleDictionary styleDictionary) {
            var styleSlots = TerminalTextLayoutOps.BuildStatusStyleSlots(line.Runs, styleDictionary);
            var popupTextSlot = TerminalTextLayoutOps.ResolveStyleSlot(styleDictionary, SurfaceStyleCatalog.CompletionPopupText);
            for (var index = 0; index < styleSlots.Length; index++) {
                if (styleSlots[index] == 0) {
                    styleSlots[index] = popupTextSlot;
                }
            }

            return new PopupContentLine(
                StyledTextLineOps.ToPlainText(line),
                styleSlots,
                []);
        }

        private static PopupContentLine[] WrapPopupInterpretationLines(PopupContentLine[] lines, int maxWidth) {
            if (lines.Length == 0 || maxWidth <= 0) {
                return [];
            }

            List<PopupContentLine> wrappedLines = [];
            foreach (PopupContentLine line in lines) {
                wrappedLines.AddRange(WrapPopupInterpretationLine(line, maxWidth));
            }

            return [.. wrappedLines];
        }

        private static PopupContentLine[] WrapPopupInterpretationLine(PopupContentLine line, int maxWidth) {
            if (maxWidth <= 0) {
                return [];
            }

            if (string.IsNullOrEmpty(line.Text)) {
                return [
                    line with {
                        StyleSlots = [],
                        BackgroundStyleSlots = [],
                    },
                ];
            }

            var text = line.Text;
            var normalizedStyles = TerminalTextLayoutOps.NormalizePopupStyleSlots(text, line.StyleSlots);
            var normalizedBackgrounds = TerminalTextLayoutOps.NormalizePopupStyleSlots(text, line.BackgroundStyleSlots);
            List<PopupContentLine> wrappedLines = [];
            StringBuilder textBuilder = new();
            List<ushort> styleBuilder = [];
            List<ushort> backgroundBuilder = [];
            var currentWidth = 0;
            var pendingWhitespaceStart = -1;
            var pendingWhitespaceLength = 0;
            var pendingWhitespaceWidth = 0;
            var cursor = 0;
            while (cursor < text.Length) {
                var tokenStart = cursor;
                var isWhitespace = char.IsWhiteSpace(text[cursor]);
                cursor += 1;
                while (cursor < text.Length && char.IsWhiteSpace(text[cursor]) == isWhitespace) {
                    cursor += 1;
                }

                var tokenLength = cursor - tokenStart;
                var tokenWidth = TerminalCellWidth.Measure(text.AsSpan(tokenStart, tokenLength));
                if (isWhitespace) {
                    if (currentWidth > 0 || wrappedLines.Count == 0) {
                        pendingWhitespaceStart = tokenStart;
                        pendingWhitespaceLength = tokenLength;
                        pendingWhitespaceWidth = tokenWidth;
                    }
                    continue;
                }

                if (CanAppendToken(tokenWidth)) {
                    AppendPendingWhitespace();
                    AppendSlice(tokenStart, tokenLength, tokenWidth);
                    continue;
                }

                if (currentWidth > 0) {
                    FlushCurrentLine();
                }

                ClearPendingWhitespace();
                if (tokenWidth <= maxWidth) {
                    AppendSlice(tokenStart, tokenLength, tokenWidth);
                    continue;
                }

                var remainingStart = tokenStart;
                while (remainingStart < cursor) {
                    var length = TerminalCellWidth.TakeLengthByCols(text, remainingStart, maxWidth, out var consumedWidth);
                    if (length <= 0) {
                        break;
                    }

                    AppendSlice(remainingStart, length, consumedWidth);
                    remainingStart += length;
                    if (remainingStart < cursor) {
                        FlushCurrentLine();
                    }
                }
            }

            FlushCurrentLine();
            return [.. wrappedLines];

            bool CanAppendToken(int tokenWidth) {
                return currentWidth + pendingWhitespaceWidth + tokenWidth <= maxWidth;
            }

            void AppendPendingWhitespace() {
                if (pendingWhitespaceLength <= 0) {
                    return;
                }

                AppendSlice(pendingWhitespaceStart, pendingWhitespaceLength, pendingWhitespaceWidth);
                ClearPendingWhitespace();
            }

            void AppendSlice(int start, int length, int width) {
                if (length <= 0) {
                    return;
                }

                textBuilder.Append(text.AsSpan(start, length));
                styleBuilder.AddRange(normalizedStyles[start..(start + length)]);
                backgroundBuilder.AddRange(normalizedBackgrounds[start..(start + length)]);
                currentWidth += width;
            }

            void FlushCurrentLine() {
                ClearPendingWhitespace();
                if (textBuilder.Length == 0) {
                    return;
                }

                wrappedLines.Add(new PopupContentLine(
                    textBuilder.ToString(),
                    [.. styleBuilder],
                    [.. backgroundBuilder]));
                textBuilder.Clear();
                styleBuilder.Clear();
                backgroundBuilder.Clear();
                currentWidth = 0;
            }

            void ClearPendingWhitespace() {
                pendingWhitespaceStart = -1;
                pendingWhitespaceLength = 0;
                pendingWhitespaceWidth = 0;
            }
        }

        private static PopupContentLine ApplyPopupContentBackground(PopupContentLine line, ushort backgroundStyleSlot) {
            var text = line.Text ?? string.Empty;
            var styles = TerminalTextLayoutOps.NormalizePopupStyleSlots(text, line.StyleSlots);
            if (string.IsNullOrEmpty(text) || backgroundStyleSlot == 0) {
                return line with {
                    Text = text,
                    StyleSlots = styles,
                    BackgroundStyleSlots = TerminalTextLayoutOps.NormalizePopupStyleSlots(text, line.BackgroundStyleSlots),
                };
            }

            return line with {
                Text = text,
                StyleSlots = styles,
                BackgroundStyleSlots = Enumerable.Repeat(backgroundStyleSlot, text.Length).ToArray(),
            };
        }

        private static PopupContentLine AddPopupHorizontalPadding(
            PopupContentLine line,
            int leftPadding,
            int rightPadding,
            ushort paddingStyleSlot = 0,
            ushort paddingBackgroundStyleSlot = 0) {
            if (leftPadding <= 0 && rightPadding <= 0) {
                return line with {
                    StyleSlots = TerminalTextLayoutOps.NormalizePopupStyleSlots(line.Text, line.StyleSlots),
                    BackgroundStyleSlots = TerminalTextLayoutOps.NormalizePopupStyleSlots(line.Text, line.BackgroundStyleSlots),
                };
            }

            var text = line.Text ?? string.Empty;
            var styles = TerminalTextLayoutOps.NormalizePopupStyleSlots(text, line.StyleSlots);
            var backgrounds = TerminalTextLayoutOps.NormalizePopupStyleSlots(text, line.BackgroundStyleSlots);
            return line with {
                Text = new string(' ', Math.Max(0, leftPadding)) + text + new string(' ', Math.Max(0, rightPadding)),
                StyleSlots = [
                    .. Enumerable.Repeat(paddingStyleSlot, Math.Max(0, leftPadding)),
                    .. styles,
                    .. Enumerable.Repeat(paddingStyleSlot, Math.Max(0, rightPadding))
                ],
                BackgroundStyleSlots = [
                    .. Enumerable.Repeat(paddingBackgroundStyleSlot, Math.Max(0, leftPadding)),
                    .. backgrounds,
                    .. Enumerable.Repeat(paddingBackgroundStyleSlot, Math.Max(0, rightPadding))
                ],
            };
        }

        private static StyledTextRun[] ResolvePopupCompletionRuns(ProjectionTextBlock content, string fallback = "") {
            var resolvedText = ResolvePopupCompletionText(content, fallback);
            if (string.IsNullOrEmpty(resolvedText)) {
                return [];
            }

            var inlineContent = ProjectionStyledTextAdapter.ToInlineSegments(content);
            if (string.IsNullOrWhiteSpace(inlineContent.Text)) {
                return [
                    new StyledTextRun {
                        Text = resolvedText,
                    },
                ];
            }

            StyledTextLine line = InlineSegmentsOps.ToStyledTextLine(inlineContent);
            return line.Runs.Length == 0
                ? [
                    new StyledTextRun {
                        Text = resolvedText,
                    },
                ]
                : [.. line.Runs.Where(static run => !string.IsNullOrEmpty(run.Text))];
        }

        private static string ResolvePopupCompletionText(ProjectionTextBlock content, string fallback = "") {
            var text = ProjectionStyledTextAdapter.ToInlineSegments(content).Text ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(text)) {
                return text;
            }

            return fallback ?? string.Empty;
        }

        private static List<PopupStyledLine> BuildPopupDetailLogicalLines(ProjectionCollectionItem item, StyleDictionary styleDictionary) {
            return SplitPopupStyledLines(ResolvePopupCompletionRuns(item.Summary), styleDictionary)
                .Where(static line => !string.IsNullOrWhiteSpace(line.Text))
                .ToList();
        }

        private static List<PopupStyledLine> SplitPopupStyledLines(StyledTextRun[] runs, StyleDictionary styleDictionary) {
            if (runs.Length == 0) {
                return [];
            }

            var text = string.Concat(runs.Select(static run => run.Text));
            if (string.IsNullOrEmpty(text)) {
                return [];
            }

            var styleSlots = TerminalTextLayoutOps.NormalizePopupStyleSlots(text, TerminalTextLayoutOps.BuildStatusStyleSlots(runs, styleDictionary));
            List<PopupStyledLine> lines = [];
            var start = 0;
            for (var index = 0; index < text.Length; index++) {
                if (text[index] != '\r' && text[index] != '\n') {
                    continue;
                }

                if (index > start) {
                    lines.Add(new PopupStyledLine(text[start..index], styleSlots[start..index]));
                }

                if (text[index] == '\r' && index + 1 < text.Length && text[index + 1] == '\n') {
                    index += 1;
                }

                start = index + 1;
            }

            if (start < text.Length) {
                lines.Add(new PopupStyledLine(text[start..], styleSlots[start..]));
            }

            return lines;
        }

        private static PopupContentLine[] WrapPopupStyledLine(
            PopupStyledLine line,
            int maxWidth,
            int maxLines,
            ushort backgroundStyleSlot,
            StyleDictionary styleDictionary) {
            if (maxWidth <= 0 || maxLines <= 0 || string.IsNullOrEmpty(line.Text)) {
                return [];
            }

            List<PopupContentLine> lines = [];
            var cursor = 0;
            while (cursor < line.Text.Length && lines.Count < maxLines) {
                var length = TerminalCellWidth.TakeLengthByCols(line.Text, cursor, maxWidth, out _);
                if (length <= 0) {
                    break;
                }

                var text = line.Text.Substring(cursor, length);
                var styleSlots = line.StyleSlots.Skip(cursor).Take(length).ToArray();
                var popupTextSlot = TerminalTextLayoutOps.ResolveStyleSlot(styleDictionary, SurfaceStyleCatalog.CompletionPopupText);
                for (var index = 0; index < styleSlots.Length; index++) {
                    if (styleSlots[index] == 0) {
                        styleSlots[index] = popupTextSlot;
                    }
                }

                lines.Add(new PopupContentLine(
                    text,
                    styleSlots,
                    Enumerable.Repeat(backgroundStyleSlot, text.Length).ToArray()));
                cursor += length;
            }

            return [.. lines];
        }

        private static List<StatusRenderLine> BuildInlineAssistRows(
            TerminalSurfaceRuntimeFrame frame,
            LineEditorRenderState inputState,
            OverlayControlDefinition assistRoot,
            int writableWidth) {
            return RenderInlineControlLines(
                assistRoot,
                inputState.Overlay,
                frame,
                inputState,
                writableWidth);
        }

        private static List<StatusRenderLine> RenderInlineControlLines(
            OverlayControlDefinition control,
            TerminalOverlay overlay,
            TerminalSurfaceRuntimeFrame frame,
            LineEditorRenderState inputState,
            int writableWidth) {
            switch (control.Kind) {
                case OverlayControlKind.Stack:
                    return RenderInlineStackLines(
                        control,
                        overlay,
                        frame,
                        inputState,
                        writableWidth);
                case OverlayControlKind.List:
                    return control.AnchorTarget == OverlayAnchorTarget.EditorBottomLeft
                        && ControlsMatch(control, inputState.OverlayPlan.CompletionControl)
                        ? BuildInlineCompletionRows(
                            frame,
                            inputState,
                            overlay.FindListTemplate(control),
                            writableWidth)
                        : [];
                case OverlayControlKind.Text:
                    return control.AnchorTarget == OverlayAnchorTarget.EditorBottomLeft
                        && ControlsMatch(control, inputState.OverlayPlan.InterpretationAssistControl)
                        ? BuildInlineInterpretationRows(
                            frame,
                            inputState.InterpretationDismissed,
                            overlay.FindTextTemplate(control),
                            writableWidth)
                        : [];
                default:
                    return [];
            }
        }

        private static List<StatusRenderLine> RenderInlineStackLines(
            OverlayControlDefinition control,
            TerminalOverlay overlay,
            TerminalSurfaceRuntimeFrame frame,
            LineEditorRenderState inputState,
            int writableWidth) {
            List<StatusRenderLine> lines = [];
            var gap = Math.Max(0, overlay.FindStackTemplate(control)?.Gap ?? 0);
            var hasPreviousGroup = false;
            foreach (var child in control.Children ?? []) {
                List<StatusRenderLine> childLines = RenderInlineControlLines(
                    child,
                    overlay,
                    frame,
                    inputState,
                    writableWidth);
                if (childLines.Count == 0) {
                    continue;
                }

                if (hasPreviousGroup) {
                    for (var index = 0; index < gap; index++) {
                        lines.Add(StatusRenderLine.FromText(string.Empty));
                    }
                }

                lines.AddRange(childLines);
                hasPreviousGroup = true;
            }

            return lines;
        }

        private static List<StatusRenderLine> BuildInlineCompletionRows(
            TerminalSurfaceRuntimeFrame frame,
            LineEditorRenderState inputState,
            ListTemplate? completionTemplate,
            int writableWidth) {
            var completion = TerminalProjectionAssistAdapter.FindCompletion(frame);
            ProjectionCollectionItem[] items = completion?.State.Items ?? [];
            if (inputState.CompletionCount <= 0 || items.Length == 0) {
                return [];
            }

            var selectedLocalIndex = completion is { State.IsPaged: true }
                ? inputState.CompletionIndex - Math.Max(0, completion.Value.State.WindowOffset) - 1
                : inputState.CompletionIndex - 1;
            if (selectedLocalIndex < 0 || selectedLocalIndex >= items.Length) {
                selectedLocalIndex = 0;
            }

            var maxRows = Math.Min(
                completionTemplate is { MaxVisibleItems: > 0 } ? completionTemplate.MaxVisibleItems : PopupCompletionMaxRows,
                items.Length);
            var start = Math.Clamp(selectedLocalIndex - (maxRows / 2), 0, Math.Max(0, items.Length - maxRows));
            List<StatusRenderLine> lines = [];
            for (var i = 0; i < maxRows; i++) {
                var itemIndex = start + i;
                ProjectionCollectionItem item = items[itemIndex];
                var selected = itemIndex == selectedLocalIndex;
                var label = ResolvePopupCompletionText(item.Label, item.ItemId);
                var secondary = ProjectionStyledTextAdapter.ToInlineSegments(item.SecondaryLabel).Text ?? string.Empty;
                var trailing = ProjectionStyledTextAdapter.ToInlineSegments(item.TrailingLabel).Text ?? string.Empty;
                var composed = $"{(selected ? ">" : " ")} {label}";
                if (!string.IsNullOrWhiteSpace(secondary)) {
                    composed += $"  {secondary}";
                }

                if (!string.IsNullOrWhiteSpace(trailing)) {
                    composed += $"  {trailing}";
                }

                lines.Add(StatusRenderLine.FromText(TerminalTextLayoutOps.FitText(composed, Math.Max(8, writableWidth))));
            }

            return lines;
        }

        private static List<StatusRenderLine> BuildInlineInterpretationRows(
            TerminalSurfaceRuntimeFrame frame,
            bool interpretationDismissed,
            TextTemplate? interpretationTemplate,
            int writableWidth) {
            var interpretation = TerminalProjectionAssistAdapter.FindInterpretation(frame);
            var interpretationSummary = ProjectionStyledTextAdapter.ToInlineSegments(
                TerminalProjectionAssistAdapter.FindInterpretationSummary(frame)?.State.Content);
            var interpretationDetail = TerminalProjectionAssistAdapter.FindInterpretationDetail(frame);
            var detailLines = ProjectionStyledTextAdapter.ToInlineSegments(interpretationDetail?.State.Lines);
            var options = interpretation?.State.Items ?? [];
            var activeInterpretationIndex = TerminalProjectionAssistAdapter.ResolveSelectedItemIndex(
                frame,
                interpretation,
                interpretationDetail);
            var activeLabel = options.Length > 1 && activeInterpretationIndex >= 0 && activeInterpretationIndex < options.Length
                ? ProjectionStyledTextAdapter.ToInlineSegments(options[activeInterpretationIndex].Label)
                : new InlineSegments();
            var hasHint = !interpretationDismissed
                && (!string.IsNullOrWhiteSpace(interpretationSummary.Text)
                    || detailLines.Any(InlineSegmentsOps.HasVisibleText)
                    || options.Length > 1);
            if (!hasHint) {
                return [];
            }

            const string lineStyleId = SurfaceStyleCatalog.StatusDetail;
            var maxInterpretationLines = Math.Max(1, interpretationTemplate?.MaxLines ?? 3);
            var maxWidth = Math.Max(8, writableWidth);
            List<StatusRenderLine> lines = [];
            if (TryBuildStatusInterpretationHeaderLine(
                interpretationSummary,
                activeLabel,
                activeInterpretationIndex,
                options.Length,
                out var headerLine,
                out var summaryRenderedInline)) {
                lines.Add(CreateStyledStatusLine(headerLine, lineStyleId, maxWidth));
            }

            if (!summaryRenderedInline
                && lines.Count < maxInterpretationLines
                && InlineSegmentsOps.HasVisibleText(interpretationSummary)) {
                lines.Add(CreateStyledStatusLine(interpretationSummary, lineStyleId, maxWidth));
            }

            if (detailLines.Length > 0
                && InlineSegmentsOps.HasVisibleText(detailLines[0])
                && lines.Count < maxInterpretationLines) {
                lines.Add(CreateStyledStatusLine(detailLines[0], lineStyleId, maxWidth));
            }

            return lines;
        }

        private static bool TryBuildStatusInterpretationHeaderLine(
            InlineSegments summary,
            InlineSegments activeLabel,
            int activeInterpretationIndex,
            int optionCount,
            out StyledTextLine line,
            out bool summaryRenderedInline) {
            summaryRenderedInline = false;
            StyledTextLineBuilder builder = new();
            var hasContent = false;
            if (optionCount > 1 && activeInterpretationIndex >= 0) {
                builder.Append($"[{activeInterpretationIndex + 1}/{optionCount}]", SurfaceStyleCatalog.InlineHint);
                hasContent = true;
            }

            if (InlineSegmentsOps.HasVisibleText(activeLabel)) {
                if (hasContent) {
                    builder.Append(": ");
                }

                builder.Append(InlineSegmentsOps.ToStyledTextLine(activeLabel));
                hasContent = true;
            }
            else if (optionCount > 1 && InlineSegmentsOps.HasVisibleText(summary)) {
                if (hasContent) {
                    builder.Append(": ");
                }

                builder.Append(InlineSegmentsOps.ToStyledTextLine(summary));
                summaryRenderedInline = true;
                hasContent = true;
            }

            line = builder.Build();
            return hasContent;
        }

        private static StatusRenderLine CreateStyledStatusLine(
            InlineSegments content,
            string lineStyleId,
            int maxWidth = int.MaxValue) {
            return CreateStyledStatusLine(InlineSegmentsOps.ToStyledTextLine(content), lineStyleId, maxWidth);
        }

        private static StatusRenderLine CreateStyledStatusLine(
            StyledTextLine line,
            string lineStyleId,
            int maxWidth = int.MaxValue) {
            var fittedLine = maxWidth == int.MaxValue
                ? line
                : new StyledTextLine {
                    Runs = TerminalTextLayoutOps.FitStyledTextRuns(line, maxWidth),
                };
            return StatusRenderLine.FromStyled(new StyledTextLine {
                LineStyleId = lineStyleId,
                Runs = fittedLine.Runs,
            });
        }

        private static bool ControlsMatch(OverlayControlDefinition control, OverlayControlDefinition? target) {
            return target is not null
                && string.Equals(control.ControlKey, target.ControlKey, StringComparison.Ordinal);
        }

        private static ushort[] BuildContentStyleSlots(
            string text,
            InlineSegments content,
            StyleDictionary styleDictionary,
            ushort fallbackStyleSlot = 0) {
            var styleSlots = Enumerable.Repeat(fallbackStyleSlot, text.Length).ToArray();
            HighlightSpan[] highlights = content.Highlights ?? [];
            foreach (HighlightSpan highlight in highlights) {
                if (highlight.Length <= 0 || string.IsNullOrWhiteSpace(highlight.StyleId)) {
                    continue;
                }

                var start = Math.Clamp(highlight.StartIndex, 0, text.Length);
                var end = Math.Clamp(highlight.EndIndex, start, text.Length);
                var slot = TerminalTextLayoutOps.ResolveStyleSlot(styleDictionary, highlight.StyleId);
                for (var index = start; index < end; index++) {
                    styleSlots[index] = slot;
                }
            }

            return styleSlots;
        }

        private static bool TryResolveMarker(
            IReadOnlyList<ClientBufferedTextMarker> markers,
            int sourceIndex,
            out ClientBufferedTextMarker marker) {
            foreach (var candidate in markers) {
                if (candidate.StartIndex == sourceIndex) {
                    marker = candidate;
                    return true;
                }
            }

            marker = null!;
            return false;
        }

        private static string ResolveMarkerDisplayText(
            ClientBufferedTextMarker marker,
            IReadOnlyList<ProjectionMarkerCatalogItem> catalog,
            string encodedText) {
            var catalogItem = ResolveMarkerCatalogItem(marker, catalog);
            if (!string.IsNullOrEmpty(catalogItem?.DisplayText)) {
                return catalogItem.DisplayText;
            }

            var start = Math.Clamp(marker.StartIndex, 0, encodedText.Length);
            var length = Math.Clamp(marker.Length, 0, encodedText.Length - start);
            return length <= 0 ? string.Empty : encodedText.Substring(start, length);
        }

        private static ushort ResolveMarkerStyleSlot(
            ClientBufferedTextMarker marker,
            IReadOnlyList<ProjectionMarkerCatalogItem> catalog,
            IReadOnlyList<ushort> contentStyleSlots) {
            var catalogItem = ResolveMarkerCatalogItem(marker, catalog);
            if (catalogItem?.Style is { Slot: > 0 } style) {
                return style.Slot;
            }

            var start = Math.Clamp(marker.StartIndex, 0, contentStyleSlots.Count);
            var end = Math.Clamp(marker.StartIndex + marker.Length, start, contentStyleSlots.Count);
            for (var index = start; index < end; index++) {
                if (contentStyleSlots[index] != 0) {
                    return contentStyleSlots[index];
                }
            }

            return 0;
        }

        private static ProjectionMarkerCatalogItem? ResolveMarkerCatalogItem(
            ClientBufferedTextMarker marker,
            IReadOnlyList<ProjectionMarkerCatalogItem> catalog) {
            var variant = marker.VariantKey ?? string.Empty;
            return catalog.FirstOrDefault(item =>
                string.Equals(item.Key, marker.Key, StringComparison.Ordinal)
                && string.Equals(item.VariantKey ?? string.Empty, variant, StringComparison.Ordinal));
        }

        private static ushort[] BuildUniformStyleSlots(string text, ushort styleSlot) {
            return string.IsNullOrEmpty(text) || styleSlot == 0
                ? []
                : Enumerable.Repeat(styleSlot, text.Length).ToArray();
        }

        private static string ResolveHeaderLineStyleId(params StyledTextLine?[] candidates) {
            return candidates
                .Select(static line => line?.LineStyleId ?? string.Empty)
                .FirstOrDefault(static styleId => !string.IsNullOrWhiteSpace(styleId))
                ?? string.Empty;
        }

        private readonly record struct StatusFrame(IReadOnlyList<StatusRenderLine> Lines, StyleDictionary StyleDictionary, int ScrollOffset);
        private readonly record struct DisplayComposition(string Text, ushort[] StyleSlots, int CursorCompositeIndex);
        private readonly record struct PopupStyledLine(string Text, ushort[] StyleSlots);
    }

    internal sealed record PanePopupMaterial(
        PopupCompletionPanel? CompletionPanel,
        PopupContentLine[] SignatureLines,
        int SignatureInnerWidth,
        ushort BorderStyleSlot,
        int HorizontalViewportMargin)
    {
        public bool HasCompletion => CompletionPanel is not null;
        public bool HasSignature => SignatureLines.Length > 0 && SignatureInnerWidth > 0;

        public int SignatureBoxInnerWidth {
            get {
                if (!HasSignature) {
                    return 0;
                }

                return Math.Max(SignatureInnerWidth, CompletionPanel?.Width - 2 ?? 0);
            }
        }

        public int CompletionSectionTopOffset => HasSignature && HasCompletion
            ? SignatureLines.Length + 1
            : 0;

        public int Width => Math.Max(
            HasSignature ? SignatureBoxInnerWidth + 2 : 0,
            CompletionPanel?.Width ?? 0);

        public int Height {
            get {
                var signatureHeight = HasSignature
                    ? SignatureLines.Length + 2
                    : 0;
                var completionHeight = CompletionPanel?.Height ?? 0;
                if (HasSignature && HasCompletion) {
                    return signatureHeight + completionHeight - 1;
                }

                return Math.Max(signatureHeight, completionHeight);
            }
        }
    }

    internal readonly record struct PopupCompletionRow(
        ProjectionCollectionItem Item,
        bool Selected,
        StyledTextRun[] LabelRuns,
        StyledTextRun[] SummaryRuns);

    internal sealed record PopupCompletionPanel(
        PopupContentLine[] ListLines,
        int ListInnerWidth,
        PopupContentLine[] DetailLines,
        int DetailInnerWidth,
        int DetailTopOffset)
    {
        public int Width => DetailLines.Length == 0 || DetailInnerWidth <= 0
            ? ListInnerWidth + 2
            : ListInnerWidth + DetailInnerWidth + 3;

        public int Height => Math.Max(
            ListLines.Length + 2,
            DetailLines.Length == 0 || DetailInnerWidth <= 0
                ? 0
                : DetailTopOffset + DetailLines.Length + 2);
    }

    internal readonly record struct PopupContentLine(
        string Text,
        ushort[] StyleSlots,
        ushort[] BackgroundStyleSlots);
}
