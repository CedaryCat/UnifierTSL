namespace UnifierTSL.ConsoleClient.Shell
{
    public readonly record struct ReadLineRenderMapOptions(
        bool EnablePaging,
        int PageSize,
        int PrefetchThreshold)
    {
        public static readonly ReadLineRenderMapOptions Unpaged = new(
            EnablePaging: false,
            PageSize: 30,
            PrefetchThreshold: 5);
    }

    public static class SemanticToRenderMapper
    {
        public static ReadLineRenderSnapshot Map(ReadLineSemanticSnapshot semantic)
        {
            return Map(semantic, state: null, ReadLineRenderMapOptions.Unpaged);
        }

        public static ReadLineRenderSnapshot Map(
            ReadLineSemanticSnapshot semantic,
            ReadLineReactiveState? state,
            ReadLineRenderMapOptions options)
        {
            ArgumentNullException.ThrowIfNull(semantic);

            ReadLineSnapshotPayload sourcePayload = semantic.Payload ?? ReadLineSnapshotPayload.CreatePlain();
            ReadLineRenderSnapshot render = new() {
                Payload = ClonePayload(sourcePayload),
                Paging = new ReadLinePagingState(),
            };
            if (!options.EnablePaging) {
                return render;
            }

            ReadLineReactiveState pagingState = state ?? new ReadLineReactiveState {
                Purpose = sourcePayload.Purpose,
                InputText = string.Empty,
                CursorIndex = 0,
                CompletionIndex = 0,
                CompletionCount = 0,
                CandidateWindowOffset = 0,
            };
            ApplyPagedCandidates(
                render.Payload,
                render.Paging,
                sourcePayload.Candidates,
                pagingState,
                options.PageSize,
                options.PrefetchThreshold);

            return render;
        }

        public static ReadLineRenderSnapshot CloneRender(ReadLineRenderSnapshot source)
        {
            ArgumentNullException.ThrowIfNull(source);

            ReadLineSnapshotPayload payload = source.Payload ?? ReadLineSnapshotPayload.CreatePlain();
            ReadLinePagingState paging = source.Paging ?? new();
            return new ReadLineRenderSnapshot {
                Payload = ClonePayload(payload),
                Paging = ClonePaging(paging),
            };
        }

        public static ReadLineSnapshotPayload ClonePayload(ReadLineSnapshotPayload source)
        {
            ArgumentNullException.ThrowIfNull(source);

            return new ReadLineSnapshotPayload {
                Purpose = source.Purpose,
                Prompt = source.Prompt,
                GhostText = source.GhostText,
                EmptySubmitBehavior = source.EmptySubmitBehavior,
                EnableCtrlEnterBypassGhostFallback = source.EnableCtrlEnterBypassGhostFallback,
                HelpText = source.HelpText,
                ParameterHint = source.ParameterHint,
                StatusPanelHeight = source.StatusPanelHeight,
                StatusLines = [.. source.StatusLines],
                AllowAnsiStatusEscapes = source.AllowAnsiStatusEscapes,
                Candidates = CloneCandidates(source.Candidates),
            };
        }

        public static ReadLinePagingState ClonePaging(ReadLinePagingState source)
        {
            ArgumentNullException.ThrowIfNull(source);

            return new ReadLinePagingState {
                Enabled = source.Enabled,
                TotalCandidateCount = source.TotalCandidateCount,
                WindowOffset = source.WindowOffset,
                PageSize = source.PageSize,
                PrefetchThreshold = source.PrefetchThreshold,
                SelectedWindowIndex = source.SelectedWindowIndex,
            };
        }

        public static List<ConsoleSuggestionItem> CloneCandidates(IEnumerable<ConsoleSuggestionItem> candidates)
        {
            ArgumentNullException.ThrowIfNull(candidates);

            return candidates.Select(static candidate => new ConsoleSuggestionItem {
                Value = candidate.Value,
                Weight = candidate.Weight,
            }).ToList();
        }

        private static void ApplyPagedCandidates(
            ReadLineSnapshotPayload payload,
            ReadLinePagingState paging,
            IReadOnlyList<ConsoleSuggestionItem> candidates,
            ReadLineReactiveState state,
            int requestedPageSize,
            int requestedThreshold)
        {
            int pageSize = Math.Max(1, requestedPageSize);
            int threshold = Math.Clamp(requestedThreshold, 0, pageSize - 1);
            int total = candidates.Count;

            if (total == 0) {
                paging.Enabled = true;
                paging.TotalCandidateCount = 0;
                paging.WindowOffset = 0;
                paging.PageSize = pageSize;
                paging.PrefetchThreshold = threshold;
                paging.SelectedWindowIndex = 0;
                payload.Candidates = [];
                return;
            }

            int maxOffset = Math.Max(0, total - pageSize);
            int requestedOffset = Math.Clamp(state.CandidateWindowOffset, 0, maxOffset);

            int selectedGlobal = -1;
            int offset;
            if (state.CompletionIndex <= 0 || state.CompletionCount <= 0) {
                offset = 0;
            }
            else {
                selectedGlobal = Math.Clamp(requestedOffset + state.CompletionIndex - 1, 0, total - 1);
                int local = selectedGlobal - requestedOffset;
                if (local <= threshold && requestedOffset > 0) {
                    offset = Math.Max(0, selectedGlobal - threshold);
                }
                else if (local >= (pageSize - threshold - 1) && requestedOffset < maxOffset) {
                    offset = Math.Min(maxOffset, selectedGlobal - (pageSize - threshold - 1));
                }
                else {
                    offset = requestedOffset;
                }
            }

            List<ConsoleSuggestionItem> page = CloneCandidates(
                candidates
                    .Skip(offset)
                    .Take(pageSize));

            int selectedWindowIndex = selectedGlobal >= 0
                ? (selectedGlobal - offset + 1)
                : 0;

            paging.Enabled = true;
            paging.TotalCandidateCount = total;
            paging.WindowOffset = offset;
            paging.PageSize = pageSize;
            paging.PrefetchThreshold = threshold;
            paging.SelectedWindowIndex = selectedWindowIndex;
            payload.Candidates = page;
        }
    }
}
