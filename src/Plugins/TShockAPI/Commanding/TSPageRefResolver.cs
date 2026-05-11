using System.Reflection;
using TShockAPI.ConsolePrompting;
using UnifierTSL.Surface.Prompting;
using UnifierTSL.Surface.Prompting.Semantics;
using UnifierTSL.Servers;

namespace TShockAPI.Commanding
{
    internal static class TSPageRefResolver
    {
        private const string PageCountMethodName = "GetPageCount";
        private static readonly Lock ResolverCacheLock = new();
        private static readonly Dictionary<Type, Func<PageRefSourceContext, int?>> PageCountResolvers = [];

        public static PromptParamExplainResult Explain(PromptParamExplainContext context) {
            var rawText = context.RawToken?.Trim() ?? string.Empty;
            if (!int.TryParse(rawText, out var pageNumber) || pageNumber < 1) {
                return Invalid();
            }

            var pageCount = GetPageCount(context.ActiveSlot, context.Server);
            if (pageCount is int knownPageCount
                && GetUpperBoundBehavior(context.ActiveSlot) == PageRefUpperBoundBehavior.ValidateKnownCount
                && pageNumber > knownPageCount) {
                return Invalid();
            }

            return Resolved(pageCount is int count
                ? $"page {pageNumber}/{count}"
                : $"page {pageNumber}");
        }

        public static IReadOnlyList<string> GetCandidates(PromptParamCandidateContext context) {
            var pageCount = GetPageCount(context.ActiveSlot, context.Server);
            if (pageCount is not int count || count < 1) {
                return [];
            }

            List<string> results = new(count);
            for (var pageNumber = 1; pageNumber <= count; pageNumber++) {
                results.Add(pageNumber.ToString());
            }

            return results;
        }

        public static int? ResolveCandidateMatchWeight(PromptParamCandidateContext context, string candidate, int baseWeight) {
            var rawText = context.RawToken ?? string.Empty;
            if (rawText.Length == 0) {
                return baseWeight;
            }

            return candidate.StartsWith(rawText, StringComparison.OrdinalIgnoreCase)
                ? ResolvePrefixMatchWeight(candidate, rawText, baseWeight)
                : null;
        }

        public static int? GetPageCount(PromptSlotSegmentSpec slot, ServerContext? server) {
            if (!TSPromptSlotMetadata.TryGetPageRefSourceType(slot, out var sourceType)) {
                return null;
            }

            return ResolvePageCount(sourceType, new PageRefSourceContext(server));
        }

        public static int CountPages(int lineCount, PaginationTools.Settings? settings = null) {
            if (lineCount <= 0) {
                return 0;
            }

            settings ??= new PaginationTools.Settings();
            var pageCount = ((lineCount - 1) / settings.MaxLinesPerPage) + 1;
            return settings.PageLimit > 0
                ? Math.Min(pageCount, settings.PageLimit)
                : pageCount;
        }

        public static PageRefUpperBoundBehavior GetUpperBoundBehavior(PromptSlotSegmentSpec slot) {
            return TSPromptSlotMetadata.GetPageRefUpperBoundBehavior(slot);
        }

        public static int? ResolvePageCount(Type sourceType, PageRefSourceContext context) {
            var resolver = GetPageCountResolver(sourceType);
            return resolver(context);
        }

        private static Func<PageRefSourceContext, int?> GetPageCountResolver(Type sourceType) {
            lock (ResolverCacheLock) {
                if (PageCountResolvers.TryGetValue(sourceType, out var cached)) {
                    return cached;
                }

                var method = sourceType.GetMethod(
                    PageCountMethodName,
                    BindingFlags.Public | BindingFlags.Static,
                    binder: null,
                    [typeof(PageRefSourceContext)],
                    modifiers: null);
                if (method is null || method.ReturnType != typeof(int?)) {
                    throw new InvalidOperationException(
                        $"Page ref source type '{sourceType.FullName}' must declare public static int? GetPageCount(PageRefSourceContext).");
                }

                var resolver = (Func<PageRefSourceContext, int?>)method.CreateDelegate(typeof(Func<PageRefSourceContext, int?>));
                PageCountResolvers[sourceType] = resolver;
                return resolver;
            }
        }

        private static int ResolvePrefixMatchWeight(string candidate, string rawText, int baseWeight) {
            if (candidate.Equals(rawText, StringComparison.OrdinalIgnoreCase)) {
                return baseWeight + 1000;
            }

            var remainingLength = Math.Max(0, candidate.Length - rawText.Length);
            return baseWeight + Math.Max(1, 256 - Math.Min(255, remainingLength));
        }

        private static PromptParamExplainResult Resolved(string displayText) => new(PromptParamExplainState.Resolved, displayText);

        private static PromptParamExplainResult Invalid() => new(PromptParamExplainState.Invalid, "invalid");
    }
}
