using Atelier.Presentation.Prompt;
using Atelier.Presentation.Window.Drafting;
using Atelier.Presentation.Window.Formatting;
using Atelier.Presentation.Window.Highlighting;
using Atelier.Session;
using UnifierTSL.Surface.Prompting.Model;
using UnifierTSL.Contracts.Projection;
using UnifierTSL.Contracts.Sessions;
using UnifierTSL.TextEditing;

namespace Atelier.Presentation.Window
{
    internal sealed partial class ReplWindowAdapter
    {
        // ClientBufferRevision is reserved for client-authored visible edits. Remote proposals are
        // accepted by advancing AcceptedRemoteRevision and are acknowledged on the next natural sync,
        // which avoids doubling packets during continuous typing. The server keeps the latest pending
        // proposal light enough to rebase for Atelier's local projection cases instead of maintaining
        // a full multi-overlay edit ledger.
        #region Sync Entry Points

        private void OnEditorStateSyncReceived(ClientBufferedEditorState bufferedState) {
            var clientState = NormalizePromptBufferedState(bufferedState);
            if (IsConsoleInteractionActive()) {
                return;
            }

            ScheduleEditorSync(clientState);
        }

        private void ScheduleEditorSync(ClientBufferedEditorState clientState) {
            lock (sync) {
                if (disposed || consoleInteractionActive) {
                    return;
                }

                latestEditorSyncState = clientState;
                latestEditorSyncSerial = checked(latestEditorSyncSerial + 1);
            }

            if (Interlocked.CompareExchange(ref editorSyncScheduled, 1, 0) == 0) {
                _ = Task.Run(ProcessEditorSyncUpdatesAsync, CancellationToken.None);
            }
        }

        private async Task ProcessEditorSyncUpdatesAsync() {
            try {
                while (true) {
                    ClientBufferedEditorState? clientState;
                    long serial;
                    lock (sync) {
                        if (disposed || latestEditorSyncState is null) {
                            Volatile.Write(ref editorSyncScheduled, 0);
                            return;
                        }

                        clientState = latestEditorSyncState;
                        serial = latestEditorSyncSerial;
                    }

                    ProcessEditorSyncState(clientState, serial);
                    await Task.Yield();

                    lock (sync) {
                        if (disposed) {
                            latestEditorSyncState = null;
                            Volatile.Write(ref editorSyncScheduled, 0);
                            return;
                        }

                        if (serial == latestEditorSyncSerial) {
                            latestEditorSyncState = null;
                            Volatile.Write(ref editorSyncScheduled, 0);
                            return;
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (processingCancellation.IsCancellationRequested) {
                Volatile.Write(ref editorSyncScheduled, 0);
            }
            catch (ObjectDisposedException) {
                Volatile.Write(ref editorSyncScheduled, 0);
            }
            catch {
                Volatile.Write(ref editorSyncScheduled, 0);
            }
        }

        private void ProcessEditorSyncState(ClientBufferedEditorState clientState, long syncSerial) {
            RemoteOverlayProposal? proposal = null;
            SessionPublication? publication = null;
            ClientBufferedEditorState sessionState = clientState;
            var publishAuthoritativeDocument = false;
            while (true) {
                var syncBase = CaptureEditorSyncBase(clientState);
                if (syncBase is null) {
                    return;
                }

                var plan = CreateEditorSyncPlan(clientState, syncBase);
                lock (sync) {
                    if (disposed || consoleInteractionActive || syncSerial != latestEditorSyncSerial) {
                        return;
                    }

                    if (syncBase.Version != editorSyncVersion) {
                        continue;
                    }

                    remoteOverlayLedger.ConfirmThrough(clientState.AcceptedRemoteRevision);
                    RecordClientBaseSnapshotLocked(clientState, plan.PairRewrite.BaseDraft, plan.ClientBaseSourceHighlights);
                    proposal = remoteOverlayLedger.UpsertProjection(
                        latestClientBase,
                        plan.ProjectedState,
                        plan.ProjectedDraft,
                        plan.ProjectedSourceHighlights);
                    if (proposal is null) {
                        latestClientBaseDraft = plan.ProjectedDraft;
                        latestClientBaseSourceHighlights = plan.ProjectedSourceHighlights;
                    }

                    sessionState = proposal?.ProjectedState ?? plan.ProjectedState;
                    publication = session.CurrentPublication;
                    publishAuthoritativeDocument = proposal is null && !plan.SourceTextChanged;
                    editorSyncVersion = checked(editorSyncVersion + 1);
                    break;
                }
            }

            if (publication is not null && proposal is not null) {
                PublishDraftDocument(
                    publication,
                    proposal.ProjectedState,
                    proposal.ProjectedDraft,
                    proposal.ProjectedSourceHighlights,
                    proposal.RemoteRevision);
            }
            else if (publishAuthoritativeDocument) {
                PublishCurrentAuthoringDocument();
            }

            ScheduleAuthoringUpdate(sessionState, syncSerial);
        }

        private void OnSubmitReceived(ClientBufferedEditorState bufferedState) {
            bufferedState = NormalizePromptBufferedState(bufferedState);
            if (TryAcceptConsoleInput(bufferedState) || IsConsoleInteractionActive()) {
                return;
            }

            lock (sync) {
                latestEditorSyncState = null;
                latestEditorSyncSerial = checked(latestEditorSyncSerial + 1);
                latestAuthoringState = null;
                latestAuthoringSerial = checked(latestAuthoringSerial + 1);
            }

            RecordSubmittedAuthoringState(bufferedState);
            EnqueueCommand(AdapterCommandKind.Submit, bufferedState, recordState: false);
        }

        private void ProcessFormatEditorCommand() {
            RemoteOverlayProposal? proposal = null;
            SessionPublication? publication = null;
            ClientBufferedEditorState? sessionState = null;
            var publishAuthoritativeDocument = false;
            while (true) {
                var formatBase = CaptureFormatBase();
                if (formatBase is null) {
                    return;
                }

                if (ReplDraftFormatter.TryRewriteAll(
                    formatBase.State,
                    formatBase.Draft,
                    ReplIndentUnit,
                    session.ParseOptions) is not { } formatRewrite) {
                    return;
                }

                var projectedSourceHighlights = HighlightProjection.InheritSourceHighlights(
                    formatBase.SourceHighlights,
                    formatRewrite.EditBatches);
                lock (sync) {
                    if (disposed || consoleInteractionActive) {
                        return;
                    }

                    if (formatBase.Version != editorSyncVersion) {
                        continue;
                    }

                    proposal = remoteOverlayLedger.UpsertProjection(
                        formatBase.BaseSnapshot,
                        formatRewrite.State,
                        formatRewrite.Draft,
                        projectedSourceHighlights);
                    if (proposal is null) {
                        latestClientBaseDraft = formatRewrite.Draft;
                        latestClientBaseSourceHighlights = [.. projectedSourceHighlights];
                        sessionState = formatRewrite.State;
                        publishAuthoritativeDocument = true;
                    }
                    else {
                        sessionState = proposal.ProjectedState;
                    }

                    publication = session.CurrentPublication;
                    editorSyncVersion = checked(editorSyncVersion + 1);
                    break;
                }
            }

            if (publication is not null && proposal is not null) {
                PublishDraftDocument(
                    publication,
                    proposal.ProjectedState,
                    proposal.ProjectedDraft,
                    proposal.ProjectedSourceHighlights,
                    proposal.RemoteRevision);
            }
            else if (publishAuthoritativeDocument) {
                PublishCurrentAuthoringDocument();
            }

            if (sessionState is not null) {
                ScheduleAuthoringUpdate(sessionState);
            }
        }

        #endregion

        #region Editor Sync Planning

        private EditorSyncBase? CaptureEditorSyncBase(ClientBufferedEditorState clientState) {
            lock (sync) {
                if (disposed || consoleInteractionActive) {
                    return null;
                }

                var previousContext = ResolveClientEditBaseLocked(clientState);
                var previousSourceHighlights = ResolveClientEditBaseSourceHighlightsLocked(clientState);
                return new EditorSyncBase(
                    editorSyncVersion,
                    previousContext,
                    [.. previousSourceHighlights],
                    CreateRemoteOverlaySnapshot(ResolvePendingOverlayForRebase(previousContext, clientState.AcceptedRemoteRevision)));
            }
        }

        private static RemoteOverlaySnapshot? CreateRemoteOverlaySnapshot(RemoteOverlayProposal? proposal) {
            return proposal is null
                ? null
                : new RemoteOverlaySnapshot(
                    proposal.RemoteRevision,
                    proposal.BaseSnapshot,
                    proposal.ProjectedState,
                    proposal.ProjectedDraft,
                    [.. proposal.ProjectedSourceHighlights]);
        }

        private EditorSyncPlan CreateEditorSyncPlan(ClientBufferedEditorState clientState, EditorSyncBase syncBase) {
            var pairRewrite = ReplPairEngine.Rewrite(
                syncBase.PreviousContext,
                clientState,
                ReplIndentUnit,
                session.ParseOptions,
                session.UseKAndRBraceStyle);
            var projectionPairRewrite = RebasePendingProjectionIfPossible(
                syncBase.PreviousContext,
                clientState,
                syncBase.PendingOverlay,
                pairRewrite,
                out var rebasedPending);
            var formatRewrite = ReplDraftFormatter.TryRewrite(
                projectionPairRewrite.State,
                projectionPairRewrite.Draft,
                projectionPairRewrite.FormatTrigger,
                session.ParseOptions);
            var projectedState = formatRewrite?.State ?? projectionPairRewrite.State;
            var projectedDraft = formatRewrite?.Draft ?? projectionPairRewrite.Draft;
            var clientBaseSourceHighlights = HighlightProjection.InheritSourceHighlights(
                syncBase.PreviousSourceHighlights,
                pairRewrite.BaseEditBatches);
            var projectionBaseHighlights = rebasedPending && syncBase.PendingOverlay is not null
                ? syncBase.PendingOverlay.ProjectedSourceHighlights
                : syncBase.PreviousSourceHighlights;
            var projectedSourceHighlights = HighlightProjection.InheritSourceHighlights(
                projectionBaseHighlights,
                projectionPairRewrite.SourceEditBatches);
            if (formatRewrite is { } formatResult) {
                projectedSourceHighlights = HighlightProjection.InheritSourceHighlights(
                    projectedSourceHighlights,
                    formatResult.EditBatches);
            }

            return new EditorSyncPlan(
                pairRewrite,
                projectedState,
                projectedDraft,
                [.. clientBaseSourceHighlights],
                [.. projectedSourceHighlights],
                !string.Equals(syncBase.PreviousContext.Draft.SourceText, projectedDraft.SourceText, StringComparison.Ordinal));
        }

        private FormatBase? CaptureFormatBase() {
            lock (sync) {
                if (disposed || consoleInteractionActive) {
                    return null;
                }

                if (remoteOverlayLedger.Latest is { } latest) {
                    return new FormatBase(
                        editorSyncVersion,
                        latest.BaseSnapshot,
                        latest.ProjectedState,
                        latest.ProjectedDraft,
                        [.. latest.ProjectedSourceHighlights]);
                }

                return new FormatBase(
                    editorSyncVersion,
                    latestClientBase,
                    latestClientBase.ToClientState(),
                    latestClientBaseDraft,
                    [.. latestClientBaseSourceHighlights]);
            }
        }

        #endregion

        #region Pending Proposal Rebase

        private RemoteOverlayProposal? ResolvePendingOverlayForRebase(DraftContext clientEditBase, long acceptedRemoteRevision) {
            var accepted = Math.Max(0, acceptedRemoteRevision);
            return remoteOverlayLedger.Latest is { } latest
                && accepted < latest.RemoteRevision
                && latest.BaseSnapshot.HasSameVisibleBuffer(clientEditBase.VisibleState)
                ? latest
                : null;
        }

        private PairRewriteResult RebasePendingProjectionIfPossible(
            DraftContext clientEditBase,
            ClientBufferedEditorState clientState,
            RemoteOverlaySnapshot? pendingOverlay,
            PairRewriteResult clientRewrite,
            out bool rebasedPending) {

            if (pendingOverlay is not null
                && clientRewrite.VisibleEdit is { } visibleEdit
                && clientRewrite.SourceEdit is { } sourceEdit
                && ReplPairEngine.TryRewritePendingProjection(
                    clientEditBase.Draft,
                    pendingOverlay.ProjectedDraft,
                    clientState,
                    visibleEdit,
                    sourceEdit,
                    ReplIndentUnit,
                    session.ParseOptions,
                    session.UseKAndRBraceStyle,
                    out var pendingRewrite)) {
                rebasedPending = true;
                return pendingRewrite;
            }

            rebasedPending = false;
            return clientRewrite;
        }

        #endregion

        #region Client Base Tracking

        private void RecordSubmittedAuthoringState(ClientBufferedEditorState bufferedState) {
            lock (sync) {
                if (disposed || consoleInteractionActive) {
                    return;
                }

                var previousContext = ResolveClientEditBaseLocked(bufferedState);
                var previousSourceHighlights = ResolveClientEditBaseSourceHighlightsLocked(bufferedState);
                var pairRewrite = ReplPairEngine.Rewrite(
                    previousContext,
                    bufferedState,
                    ReplIndentUnit,
                    session.ParseOptions,
                    session.UseKAndRBraceStyle);
                RecordClientBaseSnapshotLocked(
                    bufferedState,
                    pairRewrite.Draft,
                    HighlightProjection.InheritSourceHighlights(previousSourceHighlights, pairRewrite.SourceEditBatches));
            }
        }

        private void RecordClientBaseSnapshotLocked(ClientBufferedEditorState bufferedState) {
            RecordClientBaseSnapshotLocked(bufferedState, ReplPairEngine.DecodeBaseDraft(bufferedState), []);
        }

        private void RecordClientBaseSnapshotLocked(
            ClientBufferedEditorState bufferedState,
            DraftSnapshot draftSnapshot,
            IReadOnlyList<PromptHighlightSpan>? sourceHighlights) {
            latestClientBase = EditorBufferSnapshot.From(bufferedState);
            latestClientBaseDraft = draftSnapshot ?? ReplPairEngine.DecodeBaseDraft(bufferedState);
            latestClientBaseSourceHighlights = [.. (sourceHighlights ?? [])];
            var completionSelection = bufferedState.FindSelection(EditorProjectionSemanticKeys.AssistPrimaryList);
            var previewSelection = bufferedState.FindSelection(EditorProjectionSemanticKeys.InputGhost);
            var interpretationSelection = bufferedState.FindSelection(EditorProjectionSemanticKeys.AssistSecondaryList);
            latestRequestedCompletionIndex = Math.Max(0, completionSelection?.ActiveOrdinal ?? 0);
            latestRequestedCompletionItemId = completionSelection?.ActiveItemId ?? string.Empty;
            latestRequestedPreferredCompletionText = previewSelection?.ActiveItemId ?? string.Empty;
            latestRequestedPreferredInterpretationId = interpretationSelection?.ActiveItemId ?? string.Empty;
            editorSyncVersion = checked(editorSyncVersion + 1);
        }

        private DraftContext ResolveClientEditBaseLocked(ClientBufferedEditorState clientState) {
            var acceptedRemoteRevision = Math.Max(0, clientState.AcceptedRemoteRevision);
            if (remoteOverlayLedger.Latest is { } latest && acceptedRemoteRevision >= latest.RemoteRevision) {
                return new DraftContext(latest.ProjectedState, latest.ProjectedDraft);
            }

            return new DraftContext(latestClientBase.ToClientState(), latestClientBaseDraft);
        }

        private IReadOnlyList<PromptHighlightSpan> ResolveClientEditBaseSourceHighlightsLocked(ClientBufferedEditorState clientState) {
            var acceptedRemoteRevision = Math.Max(0, clientState.AcceptedRemoteRevision);
            if (remoteOverlayLedger.Latest is { } latest && acceptedRemoteRevision >= latest.RemoteRevision) {
                return latest.ProjectedSourceHighlights;
            }

            return latestClientBaseSourceHighlights;
        }

        #endregion

        #region Sync Utilities

        private static ClientBufferedEditorState NormalizePromptBufferedState(ClientBufferedEditorState bufferedState) {
            var normalizedText = bufferedState.BufferText ?? string.Empty;
            var normalizedMarkers = PromptProjectionBuilder.NormalizeProjectionMarkers(bufferedState.Markers, normalizedText.Length);
            var normalizedCaret = Math.Clamp(bufferedState.CaretIndex, 0, normalizedText.Length);
            var normalizedRevision = Math.Max(0, bufferedState.ClientBufferRevision);
            var normalizedAcceptedRemoteRevision = Math.Max(0, bufferedState.AcceptedRemoteRevision);
            return string.Equals(bufferedState.BufferText, normalizedText, StringComparison.Ordinal)
                && bufferedState.CaretIndex == normalizedCaret
                && bufferedState.ClientBufferRevision == normalizedRevision
                && bufferedState.AcceptedRemoteRevision == normalizedAcceptedRemoteRevision
                && EditorTextMarkerOps.ContentEquals(bufferedState.Markers, normalizedMarkers)
                ? bufferedState
                : new ClientBufferedEditorState {
                    Kind = bufferedState.Kind,
                    BufferText = normalizedText,
                    CaretIndex = normalizedCaret,
                    ClientBufferRevision = normalizedRevision,
                    AcceptedRemoteRevision = normalizedAcceptedRemoteRevision,
                    Markers = normalizedMarkers,
                    Selections = [.. (bufferedState.Selections ?? [])],
                    Collections = [.. (bufferedState.Collections ?? [])],
                };
        }

        private void EnqueueCommand(AdapterCommandKind kind, ClientBufferedEditorState bufferedState, bool recordState = true) {
            lock (sync) {
                if (disposed) {
                    return;
                }

                if (recordState) {
                    RecordClientBaseSnapshotLocked(bufferedState);
                }
            }

            commandQueue.Writer.TryWrite(new AdapterCommand(kind, BufferedState: bufferedState));
        }

        private bool IsConsoleInteractionActive() {
            lock (sync) {
                return !disposed && consoleInteractionActive;
            }
        }

        #endregion

        private sealed record EditorSyncBase(
            long Version,
            DraftContext PreviousContext,
            PromptHighlightSpan[] PreviousSourceHighlights,
            RemoteOverlaySnapshot? PendingOverlay);

        private sealed record EditorSyncPlan(
            PairRewriteResult PairRewrite,
            ClientBufferedEditorState ProjectedState,
            DraftSnapshot ProjectedDraft,
            PromptHighlightSpan[] ClientBaseSourceHighlights,
            PromptHighlightSpan[] ProjectedSourceHighlights,
            bool SourceTextChanged);

        private sealed record RemoteOverlaySnapshot(
            long RemoteRevision,
            EditorBufferSnapshot BaseSnapshot,
            ClientBufferedEditorState ProjectedState,
            DraftSnapshot ProjectedDraft,
            PromptHighlightSpan[] ProjectedSourceHighlights);

        private sealed record FormatBase(
            long Version,
            EditorBufferSnapshot BaseSnapshot,
            ClientBufferedEditorState State,
            DraftSnapshot Draft,
            PromptHighlightSpan[] SourceHighlights);
    }
}
