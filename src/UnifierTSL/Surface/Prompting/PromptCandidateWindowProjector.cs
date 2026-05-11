using UnifierTSL.Surface.Prompting.Model;
using UnifierTSL.Surface.Prompting.Runtime;

namespace UnifierTSL.Surface.Prompting {
    public readonly record struct PromptSurfaceProjectionOptions(
        bool EnablePaging,
        int PageSize,
        int PrefetchThreshold) {
        public static readonly PromptSurfaceProjectionOptions Unpaged = new(
            EnablePaging: false,
            PageSize: 80,
            PrefetchThreshold: 20);
    }

    internal sealed class PromptCandidateWindowState {
        public bool IsPaged { get; init; }
        public int TotalCandidateCount { get; init; }
        public int WindowOffset { get; init; }
        public int PageSize { get; init; } = 80;
        public int PrefetchThreshold { get; init; } = 20;
        public int SelectedWindowIndex { get; init; }
    }

    internal static class PromptCandidateWindowProjector {
        public static PromptCandidateWindowState Create(
            PromptComputation computation,
            PromptInputState? state,
            PromptSurfaceProjectionOptions options) {
            if (!options.EnablePaging) {
                return new PromptCandidateWindowState();
            }

            var pagingState = state ?? new PromptInputState();
            return CreatePagedCandidateWindow(
                computation.Suggestions,
                pagingState,
                options.PageSize,
                options.PrefetchThreshold);
        }

        public static PromptComputation CreateWindowedComputation(
            PromptComputation computation,
            PromptCandidateWindowState candidateWindow) {
            if (!candidateWindow.IsPaged) {
                return computation;
            }

            return computation with {
                Suggestions = ResolveDisplayedCandidates(computation, candidateWindow),
            };
        }

        public static PromptCompletionItem[] ResolveDisplayedCandidates(
            PromptComputation computation,
            PromptCandidateWindowState candidateWindow) {
            var candidates = computation.Suggestions?.ToArray() ?? [];
            if (!candidateWindow.IsPaged || candidates.Length == 0) {
                return candidates;
            }

            int pageSize = Math.Max(1, candidateWindow.PageSize);
            int maxOffset = Math.Max(0, candidates.Length - pageSize);
            int offset = Math.Clamp(candidateWindow.WindowOffset, 0, maxOffset);
            return [.. candidates.Skip(offset).Take(pageSize)];
        }

        private static PromptCandidateWindowState CreatePagedCandidateWindow(
            IReadOnlyList<PromptCompletionItem> candidates,
            PromptInputState state,
            int requestedPageSize,
            int requestedThreshold) {
            int pageSize = Math.Max(1, requestedPageSize);
            int threshold = Math.Clamp(requestedThreshold, 0, pageSize - 1);
            int total = candidates.Count;
            if (total == 0) {
                return new PromptCandidateWindowState {
                    IsPaged = true,
                    TotalCandidateCount = 0,
                    PageSize = pageSize,
                    PrefetchThreshold = threshold,
                };
            }

            int maxOffset = Math.Max(0, total - pageSize);
            int requestedOffset = Math.Clamp(state.CandidateWindowOffset, 0, maxOffset);
            int selectedGlobal = -1;
            int offset;
            if (state.CompletionIndex <= 0) {
                offset = 0;
            }
            else {
                selectedGlobal = Math.Clamp(state.CompletionIndex - 1, 0, total - 1);
                int local = selectedGlobal - requestedOffset;
                if (local <= threshold && requestedOffset > 0) {
                    offset = Math.Max(0, selectedGlobal - threshold);
                }
                else if (local >= pageSize - threshold - 1 && requestedOffset < maxOffset) {
                    offset = Math.Min(maxOffset, selectedGlobal - (pageSize - threshold - 1));
                }
                else {
                    offset = requestedOffset;
                }
            }

            int selectedWindowIndex = selectedGlobal >= 0
                ? selectedGlobal - offset + 1
                : 0;
            return new PromptCandidateWindowState {
                IsPaged = true,
                TotalCandidateCount = total,
                WindowOffset = offset,
                PageSize = pageSize,
                PrefetchThreshold = threshold,
                SelectedWindowIndex = selectedWindowIndex,
            };
        }
    }
}
