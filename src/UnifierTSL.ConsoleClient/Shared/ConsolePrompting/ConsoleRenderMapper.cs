namespace UnifierTSL.ConsoleClient.Shared.ConsolePrompting
{
    public readonly record struct ConsoleRenderMapOptions(
        bool EnablePaging,
        int PageSize,
        int PrefetchThreshold)
    {
        public static readonly ConsoleRenderMapOptions Unpaged = new(
            EnablePaging: false,
            PageSize: 30,
            PrefetchThreshold: 5);
    }

    public static class ConsoleRenderMapper
    {
        public static ConsoleRenderSnapshot Map(ConsolePromptSnapshot snapshot) {
            return Map(snapshot, state: null, ConsoleRenderMapOptions.Unpaged);
        }

        public static ConsoleRenderSnapshot Map(
            ConsolePromptSnapshot snapshot,
            ConsoleInputState? state,
            ConsoleRenderMapOptions options) {
            ArgumentNullException.ThrowIfNull(snapshot);

            ConsoleRenderSnapshot render = new() {
                Payload = snapshot with {
                    CommandPrefixes = [.. snapshot.CommandPrefixes],
                    StatusBodyLines = [.. snapshot.StatusBodyLines],
                    Theme = snapshot.Theme with { },
                    Candidates = [.. snapshot.Candidates.Select(static c => new ConsoleSuggestionEntry { Value = c.Value })],
                },
                Paging = new ConsoleSuggestionPageState(),
            };
            if (!options.EnablePaging) {
                return render;
            }

            ConsoleInputState pagingState = state ?? new ConsoleInputState {
                Purpose = snapshot.Purpose,
            };
            return ApplyPagedCandidates(
                render,
                snapshot.Candidates,
                pagingState,
                options.PageSize,
                options.PrefetchThreshold);
        }

        private static ConsoleRenderSnapshot ApplyPagedCandidates(
            ConsoleRenderSnapshot source,
            IReadOnlyList<ConsoleSuggestionEntry> candidates,
            ConsoleInputState state,
            int requestedPageSize,
            int requestedThreshold) {
            int pageSize = Math.Max(1, requestedPageSize);
            int threshold = Math.Clamp(requestedThreshold, 0, pageSize - 1);
            int total = candidates.Count;

            if (total == 0) {
                return source with {
                    Payload = source.Payload with {
                        Candidates = [],
                    },
                    Paging = new ConsoleSuggestionPageState {
                        Enabled = true,
                        TotalCandidateCount = 0,
                        WindowOffset = 0,
                        PageSize = pageSize,
                        PrefetchThreshold = threshold,
                        SelectedWindowIndex = 0,
                    },
                };
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

            ConsoleSuggestionEntry[] page = [.. candidates
                .Skip(offset)
                .Take(pageSize)
                .Select(static c => new ConsoleSuggestionEntry { Value = c.Value })];

            int selectedWindowIndex = selectedGlobal >= 0
                ? (selectedGlobal - offset + 1)
                : 0;

            return source with {
                Payload = source.Payload with {
                    Candidates = page,
                },
                Paging = new ConsoleSuggestionPageState {
                    Enabled = true,
                    TotalCandidateCount = total,
                    WindowOffset = offset,
                    PageSize = pageSize,
                    PrefetchThreshold = threshold,
                    SelectedWindowIndex = selectedWindowIndex,
                },
            };
        }
    }
}
