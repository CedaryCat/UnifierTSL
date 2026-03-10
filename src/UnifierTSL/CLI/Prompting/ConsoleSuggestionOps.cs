using System.Collections.Immutable;

namespace UnifierTSL.CLI.Prompting
{
    internal static class ConsoleSuggestionOps
    {
        public static ImmutableArray<ConsoleSuggestion> Normalize(IEnumerable<ConsoleSuggestion> source) {
            return [.. source
                .Where(static item => !string.IsNullOrWhiteSpace(item.Value))
                .GroupBy(static item => item.Value, StringComparer.OrdinalIgnoreCase)
                .Select(static group => group.OrderByDescending(static item => item.Weight).First())
                .OrderByDescending(static item => item.Weight)
                .ThenBy(static item => item.Value, StringComparer.OrdinalIgnoreCase)];
        }

        public static IReadOnlyList<ConsoleSuggestion> OrderUniqueByWeight(IEnumerable<ConsoleSuggestion> source) {
            return [.. Normalize(source)];
        }
    }
}
